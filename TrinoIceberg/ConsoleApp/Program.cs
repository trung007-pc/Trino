// See https://aka.ms/new-console-template for more information


using ConsoleApp;


var batchTimes = new List<long>();
batchTimes.Add(3146);
batchTimes.Add(3583);
batchTimes.Add(3318);
batchTimes.Add(6614);
batchTimes.Add(3263);
batchTimes.Add(3602);
batchTimes.Add(3407);
batchTimes.Add(4994);
batchTimes.Add(4009);
batchTimes.Add(3295);

foreach (var batchTime in batchTimes)
{
    Console.WriteLine(batchTime);
}


Console.WriteLine("trung bình");
Console.WriteLine(batchTimes.Average());
Console.WriteLine(batchTimes.Min());
Console.WriteLine(batchTimes.Max());