namespace ConsoleApp;

public class App
{
    public static bool Go(Func<bool> func)
    {
        return func.Invoke();
    }
}