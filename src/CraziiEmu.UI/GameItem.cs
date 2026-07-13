// Copyright (C) 2026 CraziiEmu Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

namespace CraziiEmu.UI;

/// <summary>
/// Represents a selectable game entry or the "Add Game" placeholder within the carousel.
/// </summary>
public class GameItem
{
    /// <summary>
    /// Gets or sets a value indicating whether this item is the placeholder card used to add new games.
    /// </summary>
    public bool IsAddCard { get; set; }

    /// <summary>
    /// Gets a value indicating whether this item is a standard playable game.
    /// </summary>
    public bool IsGameCard => !IsAddCard;

    /// <summary>
    /// Gets or sets the display title of the game.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the fully-qualified local path to the game's executable.
    /// </summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the game's cover art (e.g., from sce_sys/icon0.png).
    /// </summary>
    public Avalonia.Media.Imaging.Bitmap? CoverArt { get; set; }
}
