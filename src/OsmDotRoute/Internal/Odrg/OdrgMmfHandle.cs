using System.Buffers;
using System.IO.MemoryMappedFiles;
using Microsoft.Win32.SafeHandles;

namespace OsmDotRoute.Internal.Odrg;

/// <summary>
/// `.odrg` データを <c>byte*</c> 直アクセスで保持し、セクション本体を <see cref="ReadOnlySpan{T}"/> として
/// zero-copy で公開するハンドル（Phase 3 ステップ 3A.2、3J.2 でメモリモード追加）。
/// </summary>
/// <remarks>
/// <para>
/// 2 つのデータソースに対応する。いずれも内部表現は <c>byte*</c> 先頭ポインタ + 長さで統一され、
/// <see cref="GetSpan{T}"/> / <see cref="GetRawSpan"/> は sealed クラスの直接呼出（仮想ディスパッチなし）で
/// ホットパス性能を維持する。
/// <list type="bullet">
///   <item><see cref="Open"/>: ファイルを <see cref="MemoryMappedFile"/> でマップ（ランタイム・ファイル経路）。</item>
///   <item><see cref="CreateFromMemory"/>: マネージド <see cref="ReadOnlyMemory{Byte}"/> をピン留め（ブラウザ WASM 経路、3J.2）。
///     MMF が使えない環境で fetch 済みバイト列から直接ロードするために用いる。</item>
/// </list>
/// </para>
/// <para>
/// 利用契約: 必ず <see cref="Dispose"/> を呼ぶこと。<see cref="GetSpan{T}"/> で取得した
/// <see cref="ReadOnlySpan{T}"/> のライフタイムは本ハンドル <see cref="Dispose"/> 呼出までと
/// 一致する（<c>ReadOnlySpan&lt;T&gt;</c> 自体が <c>ref struct</c> であり、
/// コンパイラがフィールド保持・キャプチャ越えのライフタイム逸脱を弾く）。
/// </para>
/// <para>
/// ファイナライザは持たない。MMF モードの MMF / View / SafeBuffer はすべて <c>SafeHandle</c> 派生型で
/// CriticalFinalizer 経由の自動解放が効く（ユーザー判断 #21 (b)）。メモリモードの <see cref="MemoryHandle"/> も
/// 裏付けが配列なら GC ハンドル経由でピン留め・ルート化される。
/// </para>
/// </remarks>
internal sealed unsafe class OdrgMmfHandle : IDisposable
{
    // ファイルモード (CreateFromMemory では null)
    private readonly MemoryMappedFile? _mmf;
    private readonly MemoryMappedViewAccessor? _accessor;
    private readonly SafeMemoryMappedViewHandle? _viewHandle;

    // メモリモード (Open では default、未使用)
    private MemoryHandle _memoryHandle;
    private readonly bool _memoryMode;

    private readonly byte* _basePtr;
    private readonly long _viewLength;
    private bool _disposed;

    /// <summary>
    /// 指定パスの `.odrg` を読み取り専用で MMF マップし、ハンドルを構築する。
    /// </summary>
    public static OdrgMmfHandle Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var mmf = MemoryMappedFile.CreateFromFile(
            path,
            FileMode.Open,
            mapName: null,
            capacity: 0,
            MemoryMappedFileAccess.Read);
        try
        {
            var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            return new OdrgMmfHandle(mmf, accessor);
        }
        catch
        {
            mmf.Dispose();
            throw;
        }
    }

    /// <summary>
    /// マネージド <see cref="ReadOnlyMemory{Byte}"/>（fetch 済み `.odrg` バイト列など）をピン留めして
    /// ハンドルを構築する（Phase 3 ステップ 3J.2、ブラウザ WASM など MMF が使えない環境向け）。
    /// </summary>
    /// <param name="data"><c>.odrg</c> 全体のバイト列（非空）。</param>
    /// <exception cref="ArgumentException"><paramref name="data"/> が空。</exception>
    public static OdrgMmfHandle CreateFromMemory(ReadOnlyMemory<byte> data)
    {
        if (data.IsEmpty)
        {
            throw new ArgumentException(".odrg バイト列が空です。", nameof(data));
        }
        return new OdrgMmfHandle(data);
    }

    private OdrgMmfHandle(MemoryMappedFile mmf, MemoryMappedViewAccessor accessor)
    {
        _mmf = mmf;
        _accessor = accessor;
        _viewHandle = accessor.SafeMemoryMappedViewHandle;
        _viewLength = checked((long)_viewHandle.ByteLength);
        _memoryMode = false;

        byte* ptr = null;
        _viewHandle.AcquirePointer(ref ptr);
        // CreateViewAccessor は align 都合でビュー先頭が AcquirePointer 取得ポインタから
        // PointerOffset ずれる場合がある。実ファイル先頭はこの分を足す必要がある。
        _basePtr = ptr + _accessor.PointerOffset;
    }

    private OdrgMmfHandle(ReadOnlyMemory<byte> data)
    {
        _memoryMode = true;
        // 配列裏付けなら GCHandle(Pinned) でピン留め兼ルート化され、呼び出し側が参照を捨てても
        // Dispose まで移動・回収されない。
        _memoryHandle = data.Pin();
        _basePtr = (byte*)_memoryHandle.Pointer;
        _viewLength = data.Length;
    }

    /// <summary>マップ済ビューのバイト長（ファイル全長相当）。</summary>
    public long ViewLength
    {
        get
        {
            ThrowIfDisposed();
            return _viewLength;
        }
    }

    /// <summary>
    /// <see cref="OdrgSectionDirectory.Read(SafeMemoryMappedViewHandle, long)"/> に渡せる
    /// MMF ビューハンドルを公開する（ファイルモード専用）。
    /// </summary>
    /// <exception cref="InvalidOperationException">メモリモードのハンドルで呼び出した場合。</exception>
    public SafeMemoryMappedViewHandle ViewHandle
    {
        get
        {
            ThrowIfDisposed();
            if (_viewHandle is null)
            {
                throw new InvalidOperationException("ViewHandle はファイル (MMF) モードでのみ利用できます。");
            }
            return _viewHandle;
        }
    }

    /// <summary>
    /// ビューの <paramref name="byteOffset"/> から <paramref name="elementCount"/> 個の
    /// <typeparamref name="T"/> 要素を <see cref="ReadOnlySpan{T}"/> として zero-copy で取得する。
    /// </summary>
    /// <typeparam name="T">アンマネージド要素型（ファイル形式と完全一致するレイアウト）。</typeparam>
    /// <param name="byteOffset">ビュー先頭からのバイトオフセット（非負）。</param>
    /// <param name="elementCount">要素数（非負）。</param>
    public ReadOnlySpan<T> GetSpan<T>(long byteOffset, int elementCount) where T : unmanaged
    {
        ThrowIfDisposed();
        if (byteOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteOffset), byteOffset, "byteOffset must be non-negative.");
        }
        if (elementCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elementCount), elementCount, "elementCount must be non-negative.");
        }
        long byteLength = (long)elementCount * sizeof(T);
        if (byteOffset + byteLength > _viewLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(byteOffset),
                $"Span extends past view end: offset={byteOffset} bytes={byteLength} viewLen={_viewLength}.");
        }
        return new ReadOnlySpan<T>(_basePtr + byteOffset, elementCount);
    }

    /// <summary>
    /// ビューの <paramref name="byteOffset"/> から <paramref name="byteLength"/> バイトを
    /// <see cref="ReadOnlySpan{Byte}"/> として zero-copy で取得する（METADATA / TURN_RESTRICTION セクション向け）。
    /// </summary>
    public ReadOnlySpan<byte> GetRawSpan(long byteOffset, int byteLength)
    {
        ThrowIfDisposed();
        if (byteOffset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteOffset), byteOffset, "byteOffset must be non-negative.");
        }
        if (byteLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(byteLength), byteLength, "byteLength must be non-negative.");
        }
        if (byteOffset + byteLength > _viewLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(byteOffset),
                $"Span extends past view end: offset={byteOffset} bytes={byteLength} viewLen={_viewLength}.");
        }
        return new ReadOnlySpan<byte>(_basePtr + byteOffset, byteLength);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_memoryMode)
        {
            _memoryHandle.Dispose();
            return;
        }
        try
        {
            _viewHandle!.ReleasePointer();
        }
        finally
        {
            _accessor!.Dispose();
            _mmf!.Dispose();
        }
    }
}
