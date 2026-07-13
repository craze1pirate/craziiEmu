#include <stdint.h>

// Simple LCG for random numbers
static unsigned int seed = 1;
void srand(unsigned int s) { seed = s; }
unsigned int rand() {
    seed = seed * 1103515245 + 12345;
    return (unsigned int)(seed / 65536) % 32768;
}

#define SCREEN_WIDTH 1280
#define SCREEN_HEIGHT 720
#define GRID_SIZE 40
#define GRID_W (SCREEN_WIDTH / GRID_SIZE)
#define GRID_H (SCREEN_HEIGHT / GRID_SIZE)

#define COLOR_GREEN  0xFF00AA00
#define COLOR_BLUE   0xFFFF0000
#define COLOR_RED    0xFF0000FF // ARGB: Blue is FF0000? Wait, BGRA in some systems. Let's use standard RGBA/BGRA.
// In DisplayController.cs: PixelFormat.Rgba. Memory layout: R, G, B, A.
// So little-endian uint32: A B G R -> 0xAABBGGRR
#define PIXEL_COLOR(r,g,b,a) ((a << 24) | (b << 16) | (g << 8) | r)

#define COL_BG     PIXEL_COLOR(30, 180, 30, 255) // Green
#define COL_SNAKE  PIXEL_COLOR(30, 80, 255, 255) // Blue
#define COL_TONGUE PIXEL_COLOR(255, 30, 30, 255) // Red
#define COL_APPLE  PIXEL_COLOR(255, 0, 0, 255)   // Red
#define COL_ORANGE PIXEL_COLOR(255, 165, 0, 255) // Orange

// Gamepad buttons from GamepadHandler.cs
#define DPAD_UP    0x0001
#define DPAD_DOWN  0x0002
#define DPAD_LEFT  0x0004
#define DPAD_RIGHT 0x0008

struct Point {
    int x, y;
};

struct Point snake[GRID_W * GRID_H];
int snake_len = 3;
int dir_x = 1, dir_y = 0;
struct Point apple;
struct Point orange;
int orange_timer = 0;

void spawn_apple() {
    apple.x = rand() % GRID_W;
    apple.y = rand() % GRID_H;
}

void spawn_orange() {
    orange.x = rand() % (GRID_W - 1);
    orange.y = rand() % (GRID_H - 1);
    orange_timer = 100; // Orange lasts for 100 frames
}

void draw_rect(uint32_t* fb, int x, int y, int w, int h, uint32_t color) {
    for (int j = 0; j < h; j++) {
        for (int i = 0; i < w; i++) {
            int px = x + i;
            int py = y + j;
            if (px >= 0 && px < SCREEN_WIDTH && py >= 0 && py < SCREEN_HEIGHT) {
                fb[py * SCREEN_WIDTH + px] = color;
            }
        }
    }
}

void delay(int count) {
    // Spin loop delay
    volatile int i = 0;
    for (i = 0; i < count; i++) { }
}

void _start(void* args) {
    // args is a pointer to an array of 8-byte values.
    // args[6] = Framebuffer Address
    // args[7] = Input State Address
    uint64_t* args_array = (uint64_t*)args;
    uint32_t* fb = (uint32_t*)args_array[6];
    uint16_t* input = (uint16_t*)args_array[7];

    srand(1337);

    // Initialize snake
    snake[0].x = 10; snake[0].y = 10;
    snake[1].x = 9;  snake[1].y = 10;
    snake[2].x = 8;  snake[2].y = 10;

    spawn_apple();
    orange_timer = 0;

    while (1) {
        // 1. Read Input
        uint16_t btns = *input;
        if ((btns & DPAD_UP) && dir_y != 1) { dir_x = 0; dir_y = -1; }
        else if ((btns & DPAD_DOWN) && dir_y != -1) { dir_x = 0; dir_y = 1; }
        else if ((btns & DPAD_LEFT) && dir_x != 1) { dir_x = -1; dir_y = 0; }
        else if ((btns & DPAD_RIGHT) && dir_x != -1) { dir_x = 1; dir_y = 0; }

        // 2. Update Logic
        struct Point next_head = { snake[0].x + dir_x, snake[0].y + dir_y };

        // Wrap around
        if (next_head.x < 0) next_head.x = GRID_W - 1;
        if (next_head.x >= GRID_W) next_head.x = 0;
        if (next_head.y < 0) next_head.y = GRID_H - 1;
        if (next_head.y >= GRID_H) next_head.y = 0;

        // Check apple
        int grow = 0;
        if (next_head.x == apple.x && next_head.y == apple.y) {
            grow = 1;
            spawn_apple();
            if (rand() % 5 == 0) spawn_orange();
        }

        // Check orange
        if (orange_timer > 0) {
            if ((next_head.x == orange.x || next_head.x == orange.x + 1) &&
                (next_head.y == orange.y || next_head.y == orange.y + 1)) {
                grow = 3;
                orange_timer = 0;
            }
            orange_timer--;
        }

        // Move body
        int tail_idx = grow ? snake_len + grow - 1 : snake_len - 1;
        for (int i = tail_idx; i > 0; i--) {
            if (i < snake_len + grow) {
                snake[i] = snake[i - 1 >= snake_len ? snake_len - 1 : i - 1];
            }
        }
        if (grow) snake_len += grow;

        snake[0] = next_head;

        // 3. Render
        // Clear bg
        for (int i = 0; i < SCREEN_WIDTH * SCREEN_HEIGHT; i++) fb[i] = COL_BG;

        // Draw orange
        if (orange_timer > 0) {
            draw_rect(fb, orange.x * GRID_SIZE, orange.y * GRID_SIZE, GRID_SIZE * 2, GRID_SIZE * 2, COL_ORANGE);
        }

        // Draw apple
        draw_rect(fb, apple.x * GRID_SIZE, apple.y * GRID_SIZE, GRID_SIZE, GRID_SIZE, COL_APPLE);

        // Draw snake
        for (int i = 0; i < snake_len; i++) {
            draw_rect(fb, snake[i].x * GRID_SIZE + 2, snake[i].y * GRID_SIZE + 2, GRID_SIZE - 4, GRID_SIZE - 4, COL_SNAKE);
        }

        // Draw tongue
        int tx = snake[0].x * GRID_SIZE;
        int ty = snake[0].y * GRID_SIZE;
        if (dir_x == 1) draw_rect(fb, tx + GRID_SIZE, ty + GRID_SIZE/2 - 2, 10, 4, COL_TONGUE);
        else if (dir_x == -1) draw_rect(fb, tx - 10, ty + GRID_SIZE/2 - 2, 10, 4, COL_TONGUE);
        else if (dir_y == 1) draw_rect(fb, tx + GRID_SIZE/2 - 2, ty + GRID_SIZE, 4, 10, COL_TONGUE);
        else if (dir_y == -1) draw_rect(fb, tx + GRID_SIZE/2 - 2, ty - 10, 4, 10, COL_TONGUE);

        // 4. Delay (adjust as needed for emulator speed)
        delay(15000000);
    }
}
