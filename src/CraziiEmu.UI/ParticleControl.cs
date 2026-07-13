// Copyright (C) 2026 craze1pirate - CraziiEmu Project
// SPDX-License-Identifier: GPL-2.0-or-later

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace CraziiEmu.UI;

/// <summary>
/// Avalonia <see cref="Control"/> subclass that renders an ambient floating-particle
/// background. 60 semi-transparent dust motes drift in random directions, wrapping
/// around all four window edges to produce a cinematic atmosphere without distracting
/// from the foreground UI. Runs at ~60 FPS via a <see cref="DispatcherTimer"/>.
/// </summary>
public sealed class ParticleControl : Control
{
    /// <summary>Number of simultaneously active ambient particles.</summary>
    private const int ParticleCount = 60;

    /// <summary>Target frame interval for ~60 FPS updates (milliseconds).</summary>
    private const int FrameIntervalMs = 16;

    private readonly Random _rng = new();
    private readonly List<ParticleState> _particles = new(ParticleCount);
    private DispatcherTimer? _timer;

    /// <summary>
    /// Mutable per-particle state updated once per frame.
    /// </summary>
    private sealed class ParticleState
    {
        /// <summary>Horizontal position in window pixels.</summary>
        public double X;
        /// <summary>Vertical position in window pixels.</summary>
        public double Y;
        /// <summary>Circle radius in pixels (0.5 – 2.5).</summary>
        public double Radius;
        /// <summary>Horizontal drift speed in pixels per frame.</summary>
        public double SpeedX;
        /// <summary>Vertical drift speed in pixels per frame.</summary>
        public double SpeedY;
        /// <summary>White fill opacity (0.1 – 0.6).</summary>
        public double Opacity;
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SeedParticles();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FrameIntervalMs) };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer?.Stop();
        _timer = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc />
    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        
        // Scale existing particle positions into the new bounds to prevent clumping
        if (e.PreviousSize.Width > 0 && e.PreviousSize.Height > 0)
        {
            double scaleX = e.NewSize.Width / e.PreviousSize.Width;
            double scaleY = e.NewSize.Height / e.PreviousSize.Height;
            
            foreach (var p in _particles)
            {
                p.X *= scaleX;
                p.Y *= scaleY;
            }
        }
    }

    private void SeedParticles()
    {
        _particles.Clear();
        for (int i = 0; i < ParticleCount; i++)
            _particles.Add(SpawnParticle());
    }

    private ParticleState SpawnParticle()
    {
        double h = Bounds.Height > 0 ? Bounds.Height : 720;
        double w = Bounds.Width  > 0 ? Bounds.Width  : 1100;

        return new ParticleState
        {
            X       = _rng.NextDouble() * w,
            Y       = _rng.NextDouble() * h,
            Radius  = _rng.NextDouble() * 2.0 + 0.5,          // 0.5 – 2.5 px
            SpeedX  = (_rng.NextDouble() - 0.5) * 0.3,        // −0.15 – +0.15 px / frame
            SpeedY  = (_rng.NextDouble() - 0.5) * 0.3,        // −0.15 – +0.15 px / frame
            Opacity = _rng.NextDouble() * 0.5 + 0.1,          // 10 – 60 %
        };
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        double h = Bounds.Height;
        double w = Bounds.Width;
        if (w <= 0 || h <= 0) return;

        for (int i = 0; i < _particles.Count; i++)
        {
            var p = _particles[i];
            p.X += p.SpeedX;
            p.Y += p.SpeedY;

            // Wrap around all four edges
            if (p.X < 0)  p.X = w;
            if (p.X > w)  p.X = 0;
            if (p.Y < 0)  p.Y = h;
            if (p.Y > h)  p.Y = 0;
        }
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        foreach (var p in _particles)
        {
            var brush = new SolidColorBrush(Colors.White, p.Opacity);
            context.DrawEllipse(brush, null, new Point(p.X, p.Y), p.Radius, p.Radius);
        }
    }
}

