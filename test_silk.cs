using System;
using System.Reflection;
using Silk.NET.Windowing;

class Program {
    static void Main() {
        var options = WindowOptions.Default;
        var window = Window.Create(options);
        var type = window.GetType();
        foreach (var prop in typeof(IWindow).GetProperties()) {
            Console.WriteLine(prop.Name + " - " + prop.PropertyType.Name);
        }
    }
}
