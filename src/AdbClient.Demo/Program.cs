using System.Diagnostics;

namespace AdbClient.Demo
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            Process.Start("adb", "start-server");

            var client = new AdbServicesClient();
            var cts = new CancellationTokenSource(60000);
            try
            {
                await foreach (var item in client.TrackDevices(cts.Token))
                {
                    Console.WriteLine(item);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }
    }
}