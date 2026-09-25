// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using Silk.NET.Vulkan;
using CraziiEmu.Libs.Gpu;

namespace CraziiEmu.Libs.VideoOut;

internal static unsafe partial class VulkanVideoPresenter
{
    internal static float ConvertPolygonOffsetConstantFactor(
        float guestFactor,
        int negNumDbBits,
        bool dbIsFloatFmt,
        Format hostDepthFormat)
    {
        if (dbIsFloatFmt)
        {
            return guestFactor;
        }

        int hostDepthBits = hostDepthFormat switch
        {
            Format.D16Unorm or Format.D16UnormS8Uint => 16,
            Format.D24UnormS8Uint or Format.X8D24UnormPack32 => 24,
            _ => 0
        };

        if (hostDepthBits == 0)
        {
            return guestFactor;
        }

        return MathF.ScaleB(guestFactor, hostDepthBits + negNumDbBits);
    }

    public static Format GetStorageImageFormat(Format format) =>
        format switch
        {
            Format.R8Srgb => Format.R8Unorm,
            Format.R8G8Srgb => Format.R8G8Unorm,
            Format.R8G8B8A8Srgb => Format.R8G8B8A8Unorm,
            Format.BC1RgbaSrgbBlock => Format.BC1RgbaUnormBlock,
            Format.BC2SrgbBlock => Format.BC2UnormBlock,
            Format.BC3SrgbBlock => Format.BC3UnormBlock,
            Format.BC7SrgbBlock => Format.BC7UnormBlock,
            Format.R32Sint => Format.R32Uint,
            _ => format,
        };

    public static bool IsCompatibleViewFormat(Format imageFormat, Format viewFormat)
    {
        if (imageFormat == viewFormat)
        {
            return true;
        }

        var imageClass = GetFormatCompatibilityClass(imageFormat);
        return imageClass != 0 && imageClass == GetFormatCompatibilityClass(viewFormat);
    }

    private static uint GetFormatCompatibilityClass(Format format) =>
        format switch
        {
            Format.R8Unorm or
            Format.R8SNorm or
            Format.R8Srgb or
            Format.R8Uint or
            Format.R8Sint => 1,

            Format.R8G8Unorm or
            Format.R8G8SNorm or
            Format.R8G8Srgb or
            Format.R8G8Uint or
            Format.R8G8Sint or
            Format.R16Unorm or
            Format.R16SNorm or
            Format.R16Uint or
            Format.R16Sint or
            Format.R16Sfloat => 2,

            Format.R8G8B8A8Unorm or
            Format.R8G8B8A8SNorm or
            Format.R8G8B8A8Srgb or
            Format.R8G8B8A8Uint or
            Format.R8G8B8A8Sint or
            Format.B8G8R8A8Unorm or
            Format.B8G8R8A8Srgb or
            Format.R16G16Unorm or
            Format.R16G16SNorm or
            Format.R16G16Uint or
            Format.R16G16Sint or
            Format.R16G16Sfloat or
            Format.R32Uint or
            Format.R32Sint or
            Format.R32Sfloat => 4,

            Format.R16G16B16A16Unorm or
            Format.R16G16B16A16SNorm or
            Format.R16G16B16A16Uint or
            Format.R16G16B16A16Sint or
            Format.R16G16B16A16Sfloat or
            Format.R32G32Uint or
            Format.R32G32Sint or
            Format.R32G32Sfloat => 8,

            Format.R32G32B32A32Uint or
            Format.R32G32B32A32Sint or
            Format.R32G32B32A32Sfloat => 16,

            _ => 0,
        };

    public static void UnmapGpuRange(ulong address, ulong size)
    {
    }

    public static bool TrySubmitGuestImageBlit(GuestRenderTarget source, GuestRenderTarget destination)
    {
        return true;
    }
}

