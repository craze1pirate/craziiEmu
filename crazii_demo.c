// crazii_demo.c
// An animated 2D color sweep homebrew running at 60 FPS inside craziiEmu.

// Import the PS5 OS kernel sleep function (resolved at boot via our Dynamic
// Linker)
extern void sceKernelUsleep(unsigned int microseconds);

#define SCREEN_WIDTH 1280
#define SCREEN_HEIGHT 720
#define VRAM_BASE 0x200000000

void _start() {
  unsigned int *vram = (unsigned int *)VRAM_BASE;
  unsigned int color_offset = 0;

  // Standard infinite game loop
  while (1) {
    // 1. Draw a linear color gradient across all 921,600 pixels in VRAM
    for (int i = 0; i < SCREEN_WIDTH * SCREEN_HEIGHT; i++) {
      // Write a shifting RGB value with Alpha set to 255 (Opaque)
      vram[i] = (i + color_offset) | 0xFF000000;
    }

    // 2. Shift the color slightly on every frame to animate the screen
    color_offset += 256;

    // 3. Call the PS5 kernel sleep API to limit the framerate to 60 FPS
    // 16,666 microseconds = 16.6 milliseconds (~60 FPS)
    sceKernelUsleep(16666);
  }
}