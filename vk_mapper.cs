using System;
using Avalonia.Input;

public class VKMapper {
    public static void Dump() {
        foreach (Key k in Enum.GetValues(typeof(Key))) {
            Console.WriteLine(k);
        }
    }
}
