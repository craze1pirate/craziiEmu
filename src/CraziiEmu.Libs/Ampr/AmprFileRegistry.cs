// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CraziiEmu.Libs.Ampr;

public static class AmprFileRegistry
{
    private const uint CacheMagicV2 = 0x32495041u; // 'API2'
    private const uint CacheVersionV2 = 2;
    private const uint CacheMagicV3 = 0x33495041u; // 'API3'
    private const uint CacheVersionV3 = 3;

    private static readonly ConcurrentDictionary<uint, string> _hostPathsById = new(
        concurrencyLevel: Math.Max(4, Environment.ProcessorCount),
        capacity: 1_048_576);
    private static readonly object _indexGate = new();
    private static string? _indexedApp0Root;
    private static string? _indexingApp0Root;
    private static int _preloadStarted;

    public static uint Register(string guestPath, string hostPath)
    {
        if (TryGetApp0Relative(guestPath, out var relative) && relative.Length != 0)
        {
            RegisterApp0Relative(relative, hostPath);
            return ComputeFileId("$/" + relative);
        }

        var id = ComputeFileId(guestPath);
        _hostPathsById[id] = hostPath;
        return id;
    }

    public static bool TryGetHostPath(uint id, out string hostPath)
    {
        return _hostPathsById.TryGetValue(id, out hostPath!);
    }

    /// <summary>Test hook: wipe registry state between cases.</summary>
    internal static void ClearForTests()
    {
        lock (_indexGate)
        {
            _hostPathsById.Clear();
            _indexedApp0Root = null;
            _indexingApp0Root = null;
            _preloadStarted = 0;
        }
    }

    /// <summary>Test hook for the allocation-free alias publisher.</summary>
    internal static void RegisterApp0RelativeForTests(string relative, string hostPath) =>
        RegisterApp0Relative(relative, hostPath);

    /// <summary>
    /// Kick off <see cref="EnsureApp0Indexed"/> on a background thread as soon as
    /// the host knows app0.
    /// </summary>
    public static void BeginApp0IndexPreload(string? app0Root)
    {
        if (string.IsNullOrWhiteSpace(app0Root) || !Directory.Exists(app0Root))
        {
            return;
        }

        if (Interlocked.Exchange(ref _preloadStarted, 1) != 0)
        {
            return;
        }

        var root = app0Root;
        ThreadPool.UnsafeQueueUserWorkItem(
            static state =>
            {
                try
                {
                    EnsureApp0Indexed((string)state!);
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine(
                        $"[LOADER][WARN] ampr.app0_index_preload_failed: {exception.Message}");
                }
            },
            root);
    }

    /// <summary>
    /// Indexes every file under app0 under both <c>$/</c> and <c>/app0/</c> FNV
    /// ids. Cooked asset tables ship precomputed ids; without this walk, a title
    /// that never resolves those paths through APR leaves ReadFile permanently
    /// NOT_FOUND.
    /// </summary>
    public static void EnsureApp0Indexed(string app0Root)
    {
        if (string.IsNullOrWhiteSpace(app0Root))
        {
            return;
        }

        string normalizedRoot;
        try
        {
            normalizedRoot = Path.GetFullPath(app0Root);
        }
        catch
        {
            normalizedRoot = app0Root;
        }

        if (!Directory.Exists(normalizedRoot))
        {
            return;
        }

        lock (_indexGate)
        {
            if (string.Equals(_indexedApp0Root, normalizedRoot, HostFsPath.Comparison))
            {
                return;
            }

            var timer = Stopwatch.StartNew();
            var cachePath = GetIndexCachePath(normalizedRoot);
            var reindex = string.Equals(
                Environment.GetEnvironmentVariable("CRAZIIEMU_AMPR_REINDEX"),
                "1",
                StringComparison.Ordinal);

            if (!reindex &&
                cachePath is not null &&
                TryLoadIndexCache(normalizedRoot, cachePath, out var cachedCount))
            {
                _indexedApp0Root = normalizedRoot;
                _indexingApp0Root = null;
                Console.Error.WriteLine(
                    $"[LOADER][INFO] ampr.app0_index_cache_hit root={normalizedRoot} " +
                    $"files={cachedCount} ids={cachedCount * 4} elapsed_ms={timer.ElapsedMilliseconds}");
                return;
            }

            _indexingApp0Root = normalizedRoot;
            var indexedFiles = 0;
            try
            {
                var files = Directory.EnumerateFiles(
                    normalizedRoot,
                    "*",
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        MatchCasing = MatchCasing.CaseInsensitive,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint,
                    });

                var rootLength = normalizedRoot.Length;
                if (!normalizedRoot.EndsWith(Path.DirectorySeparatorChar) &&
                    !normalizedRoot.EndsWith(Path.AltDirectorySeparatorChar))
                {
                    rootLength++;
                }

                foreach (var hostPath in files)
                {
                    if (hostPath.Length <= rootLength)
                    {
                        continue;
                    }

                    var relative = hostPath[rootLength..].Replace('\\', '/');
                    if (relative.StartsWith("sce_sys/", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    RegisterApp0Relative(relative, hostPath);
                    indexedFiles++;
                }

                _indexedApp0Root = normalizedRoot;
                Console.Error.WriteLine(
                    $"[LOADER][INFO] ampr.app0_indexed root={normalizedRoot} " +
                    $"files={indexedFiles} ids={indexedFiles * 4} elapsed_ms={timer.Elapsed.TotalMilliseconds:F1}");

                if (cachePath is not null && indexedFiles > 0)
                {
                    TrySaveIndexCache(normalizedRoot, cachePath, indexedFiles);
                }
            }
            finally
            {
                _indexingApp0Root = null;
            }
        }
    }

    private static void RegisterApp0Relative(string relative, string hostPath)
    {
        var dollar = ComputeApp0AliasIds(relative, out var app0Slash, out var app0, out var bare);
        _hostPathsById[dollar] = hostPath;
        _hostPathsById[app0Slash] = hostPath;
        _hostPathsById[app0] = hostPath;
        _hostPathsById[bare] = hostPath;
    }

    private static bool TryGetApp0Relative(string guestPath, out string relative)
    {
        var normalized = guestPath.Replace('\\', '/');
        if (normalized.StartsWith("$/", StringComparison.Ordinal))
        {
            relative = normalized[2..];
            return true;
        }

        if (normalized.StartsWith("/app0/", StringComparison.OrdinalIgnoreCase))
        {
            relative = normalized[6..];
            return true;
        }

        if (normalized.StartsWith("app0/", StringComparison.OrdinalIgnoreCase))
        {
            relative = normalized[5..];
            return true;
        }

        relative = string.Empty;
        return false;
    }

    internal static uint ComputeFileId(string guestPath)
    {
        return FnvContinueUtf8(OffsetBasis, guestPath.Replace('\\', '/'));
    }

    private static string? GetIndexCachePath(string normalizedRoot)
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(appData))
            {
                return null;
            }

            var cacheDir = Path.Combine(appData, "CraziiEmu", "ampr-index");
            Directory.CreateDirectory(cacheDir);

            var rootHash = ComputeFileId(normalizedRoot);
            return Path.Combine(cacheDir, $"app0-{rootHash:x8}.v3.idx");
        }
        catch
        {
            return null;
        }
    }

    private static bool TryLoadIndexCache(
        string normalizedRoot,
        string cachePath,
        out int fileCount)
    {
        fileCount = 0;
        if (!File.Exists(cachePath))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(cachePath);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

            var magic = reader.ReadUInt32();
            var version = reader.ReadUInt32();
            var isV3 = magic == CacheMagicV3 && version == CacheVersionV3;
            var isV2 = magic == CacheMagicV2 && version == CacheVersionV2;
            if (!isV3 && !isV2)
            {
                return false;
            }

            var root = reader.ReadString();
            if (!string.Equals(root, normalizedRoot, HostFsPath.Comparison))
            {
                return false;
            }

            var expectedFiles = reader.ReadInt32();
            var expectedParamTicks = reader.ReadInt64();
            var actualParamTicks = GetParamJsonWriteTicks(normalizedRoot);
            if (actualParamTicks == 0 || actualParamTicks != expectedParamTicks)
            {
                return false;
            }

            if (expectedFiles < 0 || expectedFiles > 8_000_000)
            {
                return false;
            }

            if (isV3)
            {
                var entries = new (string Relative, uint Id0, uint Id1, uint Id2, uint Id3)[expectedFiles];
                for (var i = 0; i < expectedFiles; i++)
                {
                    entries[i] = (
                        reader.ReadString(),
                        reader.ReadUInt32(),
                        reader.ReadUInt32(),
                        reader.ReadUInt32(),
                        reader.ReadUInt32());
                }

                Parallel.ForEach(
                    entries,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount - 1),
                    },
                    entry =>
                    {
                        if (string.IsNullOrEmpty(entry.Relative) ||
                            entry.Relative.Contains("..", StringComparison.Ordinal))
                        {
                            return;
                        }

                        var hostPath = normalizedRoot.EndsWith(Path.DirectorySeparatorChar)
                            ? normalizedRoot + entry.Relative.Replace('/', Path.DirectorySeparatorChar)
                            : normalizedRoot + Path.DirectorySeparatorChar +
                              entry.Relative.Replace('/', Path.DirectorySeparatorChar);
                        _hostPathsById[entry.Id0] = hostPath;
                        _hostPathsById[entry.Id1] = hostPath;
                        _hostPathsById[entry.Id2] = hostPath;
                        _hostPathsById[entry.Id3] = hostPath;
                    });
            }
            else
            {
                var relatives = new string[expectedFiles];
                for (var i = 0; i < expectedFiles; i++)
                {
                    relatives[i] = reader.ReadString();
                }

                Parallel.ForEach(
                    relatives,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount - 1),
                    },
                    relative =>
                    {
                        if (string.IsNullOrEmpty(relative) ||
                            relative.Contains("..", StringComparison.Ordinal))
                        {
                            return;
                        }

                        var hostPath = Path.Combine(
                            normalizedRoot,
                            relative.Replace('/', Path.DirectorySeparatorChar));
                        RegisterApp0Relative(relative, hostPath);
                    });
            }

            fileCount = expectedFiles;
            return true;
        }
        catch (Exception exception)
        {
            _hostPathsById.Clear();
            Console.Error.WriteLine(
                $"[LOADER][WARN] ampr.app0_index_cache_load_failed: {exception.Message}");
            return false;
        }
    }

    private static void TrySaveIndexCache(
        string normalizedRoot,
        string cachePath,
        int fileCount)
    {
        try
        {
            var paramTicks = GetParamJsonWriteTicks(normalizedRoot);
            if (paramTicks == 0 || fileCount <= 0)
            {
                return;
            }

            var relatives = new HashSet<string>(HostFsPath.Comparer);
            foreach (var hostPath in _hostPathsById.Values)
            {
                var relative = Path.GetRelativePath(normalizedRoot, hostPath)
                    .Replace('\\', '/');
                if (string.IsNullOrEmpty(relative) ||
                    relative.StartsWith("..", StringComparison.Ordinal))
                {
                    continue;
                }

                relatives.Add(relative);
            }

            var tempPath = cachePath + ".tmp";
            using (var stream = File.Create(tempPath))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false))
            {
                writer.Write(CacheMagicV3);
                writer.Write(CacheVersionV3);
                writer.Write(normalizedRoot);
                writer.Write(relatives.Count);
                writer.Write(paramTicks);
                foreach (var relative in relatives)
                {
                    writer.Write(relative);
                    writer.Write(ComputeApp0AliasIds(relative, out var id1, out var id2, out var id3));
                    writer.Write(id1);
                    writer.Write(id2);
                    writer.Write(id3);
                }
            }

            File.Move(tempPath, cachePath, overwrite: true);
            Console.Error.WriteLine(
                $"[LOADER][INFO] ampr.app0_index_cache_saved path={cachePath} " +
                $"files={relatives.Count}");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"[LOADER][WARN] ampr.app0_index_cache_save_failed: {exception.Message}");
        }
    }

    private static uint ComputeApp0AliasIds(
        string relative,
        out uint app0Slash,
        out uint app0,
        out uint bare)
    {
        var dollar = FnvContinueUtf8(
            FnvContinueAscii(FnvContinueAscii(OffsetBasis, (byte)'$'), (byte)'/'),
            relative);
        app0Slash = FnvContinueUtf8(FnvContinueAsciiPrefix(OffsetBasis, "/app0/"u8), relative);
        app0 = FnvContinueUtf8(FnvContinueAsciiPrefix(OffsetBasis, "app0/"u8), relative);
        bare = FnvContinueUtf8(OffsetBasis, relative);
        return dollar;
    }

    private static long GetParamJsonWriteTicks(string normalizedRoot)
    {
        try
        {
            var paramPath = Path.Combine(normalizedRoot, "sce_sys", "param.json");
            return File.Exists(paramPath)
                ? File.GetLastWriteTimeUtc(paramPath).Ticks
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private const uint OffsetBasis = 2166136261;
    private const uint FnvPrime = 16777619;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint FnvContinueAscii(uint hash, byte value)
    {
        hash ^= value;
        return hash * FnvPrime;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint FnvContinueAsciiPrefix(uint hash, ReadOnlySpan<byte> ascii)
    {
        foreach (var value in ascii)
        {
            hash ^= value;
            hash *= FnvPrime;
        }

        return hash;
    }

    private static uint FnvContinueUtf8(uint hash, string text)
    {
        Span<byte> utf8Scratch = stackalloc byte[4];
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch < 0x80)
            {
                hash ^= (byte)ch;
                hash *= FnvPrime;
                continue;
            }

            var written = Encoding.UTF8.GetBytes(text.AsSpan(i, 1), utf8Scratch);
            for (var b = 0; b < written; b++)
            {
                hash ^= utf8Scratch[b];
                hash *= FnvPrime;
            }
        }

        return hash;
    }
}
