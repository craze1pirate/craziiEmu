// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using Silk.NET.Windowing;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Input;
using CraziiEmu.Core.Memory;

namespace CraziiEmu.Core.Gpu;

/// <summary>
/// Handles the host-side visual representation of the guest's virtual framebuffer.
/// Creates a native Silk.NET window with its own blocking render loop that blits
/// the guest VRAM contents to an OpenGL texture each frame.
/// </summary>
public class DisplayController : IDisposable
{
    /// <summary>
    /// The starting virtual address mapping precisely to the GPU Heap in the VirtualMemoryManager.
    /// </summary>
    public const ulong GUEST_VRAM_START = 0x200000000;

    public static DisplayController? Active { get; private set; }

    private int _width = 1280;
    private int _height = 720;
    private int _pitch = 1280;

    private readonly VirtualMemoryManager _vmm;
    private IWindow? _window;
    private GL? _gl;
    private uint _texture;
    private uint _fbo;
    private bool _disposed;
    private byte[] _frontBuffer = new byte[1280 * 720 * 4];
    private byte[] _backBuffer = new byte[1280 * 720 * 4];
    private readonly object _bufferLock = new object();

    /// <summary>
    /// Gets a value indicating whether the display window is currently open and active.
    /// </summary>
    public bool IsWindowOpen => _window != null && !_window.IsClosing;

    /// <summary>
    /// Initializes a new instance of the <see cref="DisplayController"/> class.
    /// </summary>
    /// <param name="vmm">The virtual memory manager instance containing the GPU heap.</param>
    public DisplayController(VirtualMemoryManager vmm)
    {
        _vmm = vmm ?? throw new ArgumentNullException(nameof(vmm));
        Active = this;
    }

    /// <summary>
    /// Creates the Silk.NET window and enters the native, blocking render loop.
    /// This method blocks the calling thread until the window is closed by the user.
    /// The Silk.NET event loop drives <see cref="OnRender"/> each frame, ensuring
    /// the OpenGL context and swap-chain are managed correctly.
    /// </summary>
    public void InitializeWindow()
    {
        var options = WindowOptions.Default;
        options.Size = new Vector2D<int>(_width, _height);
        options.Title = "CraziiEmu Display";
        options.ShouldSwapAutomatically = true;

        // Enable hardware V-Sync to prevent tearing
        options.VSync = true;

        _window = Window.Create(options);

        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.Closing += OnClose;
        _window.FramebufferResize += OnFramebufferResize;

        // Blocks the calling thread — Silk.NET drives the native render loop,
        // processing input events and calling OnRender each frame.
        _window.Run();
    }

    /// <summary>
    /// Called once when the window's OpenGL context is ready.
    /// Allocates the GPU texture and framebuffer object used for blitting.
    /// </summary>
    private unsafe void OnLoad()
    {
        if (_window == null) return;

        var input = _window.CreateInput();
        foreach (var keyboard in input.Keyboards)
        {
            keyboard.KeyDown += (kb, key, arg3) =>
            {
                if (key == Key.F11)
                {
                    _window.WindowState = _window.WindowState == WindowState.Fullscreen 
                        ? WindowState.Normal : WindowState.Fullscreen;
                }
            };
        }

        _gl = GL.GetApi(_window);

        // Create the texture that will hold each frame's pixel data
        _texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _texture);

        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

        // Pre-allocate the texture storage
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8,
            (uint)_width, (uint)_height, 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, ReadOnlySpan<byte>.Empty);

        // Create a read-only FBO backed by our texture for BlitFramebuffer
        _fbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _fbo);
        _gl.FramebufferTexture2D(FramebufferTarget.ReadFramebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _texture, 0);
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
    }

    private void OnFramebufferResize(Vector2D<int> size)
    {
        if (_gl != null)
        {
            _gl.Viewport(size);
        }
    }

    /// <summary>
    /// Retrieves the raw 3.68MB pixel byte block from the virtual memory manager, validating the VRAM bounds.
    /// Used for headless testing of the memory access pipeline without requiring a physical OpenGL context.
    /// </summary>
    public void UpdateFrameBufferOnly()
    {
        var span = _vmm.GetSpan(GUEST_VRAM_START, _pitch * _height * 4);

        // Touch the memory to ensure it doesn't get optimized away
        if (span.Length > 0)
        {
            _ = span[0];
        }
    }

    public void SubmitFrame(ReadOnlySpan<byte> vramSpan, int width, int height, int pitch)
    {
        if (width != _width || height != _height || pitch != _pitch)
        {
            lock (_bufferLock)
            {
                _width = width;
                _height = height;
                _pitch = pitch;
                _frontBuffer = new byte[_pitch * _height * 4];
                _backBuffer = new byte[_pitch * _height * 4];

                _window?.Invoke(() =>
                {
                    if (_window != null && _gl != null)
                    {
                        _window.Size = new Vector2D<int>(_width, _height);
                        
                        _gl.BindTexture(TextureTarget.Texture2D, _texture);
                        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba, (uint)_width, (uint)_height, 0, PixelFormat.Bgra, PixelType.UnsignedByte, ReadOnlySpan<byte>.Empty);
                        
                        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _fbo);
                        _gl.FramebufferTexture2D(FramebufferTarget.ReadFramebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _texture, 0);
                        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
                    }
                });
            }
        }

        vramSpan.CopyTo(_backBuffer);

        lock (_bufferLock)
        {
            var temp = _frontBuffer;
            _frontBuffer = _backBuffer;
            _backBuffer = temp;
        }
    }

    /// <summary>
    /// Called by the native Silk.NET render loop each frame.
    /// Clears the screen, uploads the guest VRAM to the texture, and blits it
    /// to the default framebuffer (the window surface).
    /// </summary>
    /// <param name="delta">Elapsed time in seconds since the last frame.</param>
    private unsafe void OnRender(double delta)
    {
        if (_gl == null) return;

        _gl.Clear(ClearBufferMask.ColorBufferBit);

        byte[] bufferToRender;
        lock (_bufferLock)
        {
            bufferToRender = _frontBuffer;
        }

        fixed (byte* ptr = bufferToRender)
        {
            _gl.PixelStore(PixelStoreParameter.UnpackRowLength, _pitch);
            
            _gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0,
                (uint)_width, (uint)_height,
                PixelFormat.Bgra, PixelType.UnsignedByte, ptr);
                
            _gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
        }

        // Blit the texture-backed FBO to the default (window) framebuffer
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _fbo);
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);

        _gl.BlitFramebuffer(
            0, 0, _width, _height,
            0, 0, _window!.FramebufferSize.X, _window.FramebufferSize.Y,
            ClearBufferMask.ColorBufferBit,
            BlitFramebufferFilter.Linear);

        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
    }

    /// <summary>
    /// Called when the window is closing. Releases OpenGL resources while the
    /// GL context is still current, before the window is destroyed.
    /// </summary>
    private void OnClose()
    {
        if (_gl != null)
        {
            _gl.DeleteFramebuffer(_fbo);
            _gl.DeleteTexture(_texture);
            _gl.Dispose();
            _gl = null;
        }
    }

    /// <summary>
    /// Disposes of any remaining resources. Safe to call multiple times.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        OnClose();
        _window?.Dispose();
        GC.SuppressFinalize(this);
    }
}
