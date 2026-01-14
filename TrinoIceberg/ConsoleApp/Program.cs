// See https://aka.ms/new-console-template for more information


using ConsoleApp;

var lambda = () =>
{
    return true;
};

var result = App.Go(lambda);


Console.WriteLine($"Hello, World!{result}");