namespace ConsoleApp;

public class App
{
    public static bool Go(Func<bool> func)
    {
        return func.Invoke();
    }
    
    public async Task Run(string name)
    {
        Console.WriteLine($"[{name}] Started on Thread {Environment.CurrentManagedThreadId}");
       Thread.Sleep(2000);
        Console.WriteLine($"hello world: {name}");
        Console.WriteLine($"[{name}] Completed on Thread {Environment.CurrentManagedThreadId}");
    }
}