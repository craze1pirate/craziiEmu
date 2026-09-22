// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later
// Referred from KytyPS5 project

using System;
using System.Collections.Generic;
using System.Linq;
using CraziiEmu.Libs.Gpu;

namespace CraziiEmu.Libs.VideoOut;

internal enum GuestDepthExtentResolutionKind
{
    Exact,
    TextureAlias,
    StaleOneByOne,
    Mismatch,
}

internal readonly record struct GuestDepthExtentResolution(
    GuestDepthExtentResolutionKind Kind,
    uint Width,
    uint Height)
{
    public bool IsUsable => Kind != GuestDepthExtentResolutionKind.Mismatch;
}

internal static class GuestDepthExtentResolver
{
    public static GuestDepthExtentResolution Resolve(
        GuestDepthTarget depth,
        uint colorWidth,
        uint colorHeight,
        IReadOnlyList<GuestDrawTexture> textures)
    {
        if (depth.Width >= colorWidth && depth.Height >= colorHeight)
        {
            return new GuestDepthExtentResolution(
                GuestDepthExtentResolutionKind.Exact,
                depth.Width,
                depth.Height);
        }

        var matchingTexture = textures.FirstOrDefault(texture =>
            (texture.Address == depth.Address ||
             texture.Address == depth.ReadAddress ||
             texture.Address == depth.WriteAddress) &&
            texture.Width >= colorWidth &&
            texture.Height >= colorHeight);
        if (matchingTexture is not null)
        {
            return new GuestDepthExtentResolution(
                GuestDepthExtentResolutionKind.TextureAlias,
                matchingTexture.Width,
                matchingTexture.Height);
        }

        var overlappingTexture = textures.FirstOrDefault(texture =>
        {
            if (texture.Width < colorWidth || texture.Height < colorHeight || texture.Address == 0)
            {
                return false;
            }

            var textureSize = VulkanVideoPresenter.GetGuestImageByteCount(
                texture.Format,
                texture.Pitch > 0 ? Math.Max(texture.Pitch, texture.Width) : texture.Width,
                texture.Height,
                texture.Depth);
            var depthSize = (ulong)depth.Width * depth.Height * (depth.GuestFormat == 1 ? 2UL : 4UL);

            return GuestTexturePageTracker<object>.ImageRangeOverlaps(
                texture.Address, textureSize, depth.Address, depthSize);
        });

        if (overlappingTexture is not null)
        {
            return new GuestDepthExtentResolution(
                GuestDepthExtentResolutionKind.TextureAlias,
                overlappingTexture.Width,
                overlappingTexture.Height);
        }

        if (depth.Width == 1 && depth.Height == 1)
        {
            return new GuestDepthExtentResolution(
                GuestDepthExtentResolutionKind.StaleOneByOne,
                colorWidth,
                colorHeight);
        }

        return new GuestDepthExtentResolution(
            GuestDepthExtentResolutionKind.Mismatch,
            depth.Width,
            depth.Height);
    }
}
