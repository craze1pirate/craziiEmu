// Copyright (C) 2026 SharpEmu Emulator Project
// Copyright (C) 2026 CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.Libs.VideoOut;

using System;
using CraziiEmu.Libs.Metrics;
using CraziiEmu.Libs.VideoOut.Overlay;
using Silk.NET.Vulkan;
using VkBuffer = Silk.NET.Vulkan.Buffer;

internal static unsafe partial class VulkanVideoPresenter
{
    private sealed partial class Presenter
    {
        // This partial manages presentation of the performance overlay.
        private VkBuffer[] _overlayStagingBuffers = [];
        private DeviceMemory[] _overlayStagingMemory = [];
        private nint[] _overlayStagingMapped = [];

        private void CreateOverlayResources()
        {
            const ulong overlayBytes = 512 * 512 * 4;
            _overlayStagingBuffers = new VkBuffer[MaxFramesInFlight];
            _overlayStagingMemory = new DeviceMemory[MaxFramesInFlight];
            _overlayStagingMapped = new nint[MaxFramesInFlight];
            for (var slot = 0; slot < MaxFramesInFlight; slot++)
            {
                _overlayStagingBuffers[slot] = CreateBuffer(
                    overlayBytes,
                    BufferUsageFlags.TransferSrcBit,
                    MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit,
                    out _overlayStagingMemory[slot]);
                void* mapped;
                Check(
                    _vk.MapMemory(_device, _overlayStagingMemory[slot], 0, overlayBytes, 0, &mapped),
                    "vkMapMemory(overlay staging)");
                _overlayStagingMapped[slot] = (nint)mapped;
            }
        }

        private void RecordOverlayBlit(uint imageIndex, int frameSlot)
        {
            if (OverlayRenderer.Mode == OverlayMode.Off ||
                _overlayStagingMapped.Length <= frameSlot)
            {
                return;
            }

            var (startX, startY, width, height) = LayoutEngine.ComputeLayout(
                OverlayRenderer.Mode,
                OverlayRenderer.Position,
                _extent.Width,
                _extent.Height);

            int panelW = Math.Clamp((int)MathF.Ceiling(width), 1, 512);
            int panelH = Math.Clamp((int)MathF.Ceiling(height), 1, 512);
            int dstX = Math.Clamp((int)startX, 0, Math.Max(0, (int)_extent.Width - panelW));
            int dstY = Math.Clamp((int)startY, 0, Math.Max(0, (int)_extent.Height - panelH));

            var panelPixels = new Span<uint>((void*)_overlayStagingMapped[frameSlot], panelW * panelH);
            MetricsManager.RefreshStatsIfDue(0, 0);
            OverlayRenderer.RenderToBuffer(panelPixels, panelW, panelH);

            var targetImage = PresentationTargetImage(imageIndex);
            var overlaySwapchainToDst = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                SrcAccessMask = 0,
                DstAccessMask = AccessFlags.TransferWriteBit,
                OldLayout = PresentationTargetFinalLayout,
                NewLayout = ImageLayout.TransferDstOptimal,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = targetImage,
                SubresourceRange = ColorSubresourceRange(),
            };
            _vk.CmdPipelineBarrier(
                _commandBuffer,
                PipelineStageFlags.ColorAttachmentOutputBit | PipelineStageFlags.TransferBit,
                PipelineStageFlags.TransferBit,
                0, 0, null, 0, null, 1, &overlaySwapchainToDst);

            var overlayCopyRegion = new BufferImageCopy
            {
                BufferOffset = 0,
                BufferRowLength = (uint)panelW,
                BufferImageHeight = (uint)panelH,
                ImageSubresource = new ImageSubresourceLayers(ImageAspectFlags.ColorBit, 0, 0, 1),
                ImageOffset = new Offset3D { X = dstX, Y = dstY, Z = 0 },
                ImageExtent = new Extent3D { Width = (uint)panelW, Height = (uint)panelH, Depth = 1 },
            };

            _vk.CmdCopyBufferToImage(
                _commandBuffer,
                _overlayStagingBuffers[frameSlot],
                targetImage,
                ImageLayout.TransferDstOptimal,
                1,
                &overlayCopyRegion);

            var swapchainToFinal = new ImageMemoryBarrier
            {
                SType = StructureType.ImageMemoryBarrier,
                SrcAccessMask = AccessFlags.TransferWriteBit,
                DstAccessMask = AccessFlags.MemoryReadBit,
                OldLayout = ImageLayout.TransferDstOptimal,
                NewLayout = PresentationTargetFinalLayout,
                SrcQueueFamilyIndex = Vk.QueueFamilyIgnored,
                DstQueueFamilyIndex = Vk.QueueFamilyIgnored,
                Image = targetImage,
                SubresourceRange = ColorSubresourceRange(),
            };
            _vk.CmdPipelineBarrier(
                _commandBuffer,
                PipelineStageFlags.TransferBit,
                PipelineStageFlags.BottomOfPipeBit,
                0, 0, null, 0, null, 1, &swapchainToFinal);
        }
    }
}
