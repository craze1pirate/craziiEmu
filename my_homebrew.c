// A simple C program that writes to the framebuffer address
void main() {
    // 0x200000000 is our virtual GPU Framebuffer address
    unsigned int* vram = (unsigned int*)0x200000000;
    
    // Fill the first 100 pixels with solid Blue/Cyan (0x0000FFFF)
    for (int i = 0; i < 100; i++) {
        vram[i] = 0x0000FFFF;
    }
    
    // Inline assembly syscall to exit cleanly
    asm("mov $60, %rax;"  // sys_exit opcode
        "mov $0, %rdi;"   // exit code 0
        "syscall;");
}