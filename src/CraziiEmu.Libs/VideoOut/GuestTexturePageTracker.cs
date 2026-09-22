// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Collections.Generic;

namespace CraziiEmu.Libs.VideoOut;

/// <summary>
/// Sparse multi-level page table for guest GPU texture and surface range tracking.
/// Reconstructed from KytyPS5's MultiLevelPageTable with 1 MiB page granularity
/// across the 40-bit address space, featuring transparent 48-bit fallback.
/// </summary>
/// <typeparam name="T">The type of resource stored in the page table.</typeparam>
public sealed class GuestTexturePageTracker<T> where T : class
{
    public const int PageBits = 20;
    public const ulong PageSize = 1UL << PageBits; // 1 MiB
    public const ulong PageMask = PageSize - 1;

    public const int AddressSpaceBits = 40;
    public const int FirstLevelBits = 10;
    public const int SecondLevelBits = AddressSpaceBits - FirstLevelBits - PageBits; // 10 bits

    public const int FirstLevelEntries = 1 << FirstLevelBits; // 1024
    public const int BucketEntries = 1 << SecondLevelBits;     // 1024
    public const ulong PageCount = 1UL << (AddressSpaceBits - PageBits); // 1,048,576 pages
    public const ulong AddressSpaceSize = 1UL << AddressSpaceBits;       // 1 TiB

    private readonly List<T>?[]?[] _firstLevel = new List<T>?[]?[FirstLevelEntries];
    private Dictionary<ulong, List<T>>? _highAddressPages;
    private readonly HashSet<T> _queriedSet = new();

    /// <summary>
    /// Checks whether two memory byte ranges overlap (half-open intervals: [addr, addr + size)).
    /// </summary>
    public static bool ImageRangeOverlaps(ulong left, ulong leftSize, ulong right, ulong rightSize)
    {
        if (leftSize == 0 || rightSize == 0 || left > ulong.MaxValue - leftSize || right > ulong.MaxValue - rightSize)
        {
            return false;
        }

        return left < right + rightSize && right < left + leftSize;
    }

    /// <summary>
    /// Checks whether the outer range completely encloses the inner range.
    /// </summary>
    public static bool ImageRangeEncloses(ulong outerAddr, ulong outerSize, ulong innerAddr, ulong innerSize)
    {
        if (outerSize == 0 || innerSize == 0 || outerAddr > ulong.MaxValue - outerSize || innerAddr > ulong.MaxValue - innerSize)
        {
            return false;
        }

        return outerAddr <= innerAddr && (outerAddr + outerSize) >= (innerAddr + innerSize);
    }

    /// <summary>
    /// Computes the half-open page index range [firstPage, lastPageExclusive) covered by [address, address + size).
    /// </summary>
    public static bool TryGetPageRange(ulong address, ulong size, out ulong firstPage, out ulong lastPageExclusive)
    {
        if (size == 0 || address > ulong.MaxValue - size)
        {
            firstPage = 0;
            lastPageExclusive = 0;
            return false;
        }

        firstPage = address >> PageBits;
        lastPageExclusive = ((address + size - 1) >> PageBits) + 1;
        return true;
    }

    /// <summary>
    /// Registers a resource across all 1 MiB pages it intersects.
    /// </summary>
    public void Register(ulong address, ulong size, T item)
    {
        if (!TryGetPageRange(address, size, out var firstPage, out var lastPageExclusive))
        {
            return;
        }

        for (var page = firstPage; page < lastPageExclusive; page++)
        {
            var list = GetOrCreatePageList(page);
            if (!list.Contains(item))
            {
                list.Add(item);
            }
        }
    }

    /// <summary>
    /// Unregisters a resource from all 1 MiB pages it intersects.
    /// </summary>
    public bool Unregister(ulong address, ulong size, T item)
    {
        if (!TryGetPageRange(address, size, out var firstPage, out var lastPageExclusive))
        {
            return false;
        }

        var removedAny = false;
        for (var page = firstPage; page < lastPageExclusive; page++)
        {
            var list = FindPageList(page);
            if (list != null && list.Remove(item))
            {
                removedAny = true;
            }
        }

        return removedAny;
    }

    /// <summary>
    /// Finds all unique resources overlapping [address, address + size).
    /// </summary>
    public List<T> FindOverlapping(
        ulong address,
        ulong size,
        Func<T, (ulong Address, ulong Size)> rangeSelector)
    {
        var results = new List<T>();
        FindOverlapping(address, size, rangeSelector, results);
        return results;
    }

    /// <summary>
    /// Populates the destination list with all unique resources overlapping [address, address + size).
    /// </summary>
    public void FindOverlapping(
        ulong address,
        ulong size,
        Func<T, (ulong Address, ulong Size)> rangeSelector,
        List<T> destination)
    {
        if (!TryGetPageRange(address, size, out var firstPage, out var lastPageExclusive))
        {
            return;
        }

        _queriedSet.Clear();
        for (var page = firstPage; page < lastPageExclusive; page++)
        {
            var list = FindPageList(page);
            if (list == null)
            {
                continue;
            }

            for (var i = 0; i < list.Count; i++)
            {
                var candidate = list[i];
                if (_queriedSet.Add(candidate))
                {
                    var (candAddr, candSize) = rangeSelector(candidate);
                    if (ImageRangeOverlaps(candAddr, candSize, address, size))
                    {
                        destination.Add(candidate);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Finds a resource that completely encloses [address, address + size).
    /// If multiple match, returns the closest (smallest enclosing) candidate.
    /// </summary>
    public T? FindEnclosing(
        ulong address,
        ulong size,
        Func<T, (ulong Address, ulong Size)> rangeSelector)
    {
        if (!TryGetPageRange(address, size, out var firstPage, out var lastPageExclusive))
        {
            return null;
        }

        T? best = null;
        ulong bestSize = ulong.MaxValue;

        _queriedSet.Clear();
        for (var page = firstPage; page < lastPageExclusive; page++)
        {
            var list = FindPageList(page);
            if (list == null)
            {
                continue;
            }

            for (var i = 0; i < list.Count; i++)
            {
                var candidate = list[i];
                if (_queriedSet.Add(candidate))
                {
                    var (candAddr, candSize) = rangeSelector(candidate);
                    if (ImageRangeEncloses(candAddr, candSize, address, size))
                    {
                        if (candSize < bestSize)
                        {
                            best = candidate;
                            bestSize = candSize;
                        }
                    }
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Unmaps all resources overlapping [address, address + size), removing them from the tracker
    /// and invoking an optional eviction action on each evicted resource.
    /// </summary>
    public List<T> UnmapRange(
        ulong address,
        ulong size,
        Func<T, (ulong Address, ulong Size)> rangeSelector,
        Action<T>? onEvict = null)
    {
        var evicted = FindOverlapping(address, size, rangeSelector);
        for (var i = 0; i < evicted.Count; i++)
        {
            var item = evicted[i];
            var (candAddr, candSize) = rangeSelector(item);
            Unregister(candAddr, candSize, item);
            onEvict?.Invoke(item);
        }

        return evicted;
    }

    /// <summary>
    /// Clears all page mappings.
    /// </summary>
    public void Clear()
    {
        Array.Clear(_firstLevel);
        _highAddressPages?.Clear();
        _queriedSet.Clear();
    }

    private List<T> GetOrCreatePageList(ulong page)
    {
        if (page < PageCount)
        {
            var l1 = (int)(page >> SecondLevelBits);
            var l2 = (int)(page & (BucketEntries - 1));
            var bucket = _firstLevel[l1];
            if (bucket == null)
            {
                bucket = new List<T>?[BucketEntries];
                _firstLevel[l1] = bucket;
            }

            var list = bucket[l2];
            if (list == null)
            {
                list = new List<T>(4);
                bucket[l2] = list;
            }

            return list;
        }

        _highAddressPages ??= new Dictionary<ulong, List<T>>();
        if (!_highAddressPages.TryGetValue(page, out var highList))
        {
            highList = new List<T>(4);
            _highAddressPages[page] = highList;
        }

        return highList;
    }

    private List<T>? FindPageList(ulong page)
    {
        if (page < PageCount)
        {
            var l1 = (int)(page >> SecondLevelBits);
            var bucket = _firstLevel[l1];
            if (bucket == null)
            {
                return null;
            }

            var l2 = (int)(page & (BucketEntries - 1));
            return bucket[l2];
        }

        if (_highAddressPages != null && _highAddressPages.TryGetValue(page, out var highList))
        {
            return highList;
        }

        return null;
    }
}
