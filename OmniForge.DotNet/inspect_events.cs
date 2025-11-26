using System;
using System.Linq;
using System.Reflection;
using TwitchLib.EventSub.Websockets;

class Program
{
    static void Main()
    {
        var type = typeof(EventSubWebsocketClient);
        foreach (var evt in type.GetEvents())
        {
            Console.WriteLine($"Event: {evt.Name}, Type: {evt.EventHandlerType}");
        }
    }
}
