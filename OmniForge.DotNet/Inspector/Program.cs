using System;
using System.Linq;
using System.Reflection;
using TwitchLib.EventSub.Websockets;

class Program
{
    static void Main()
    {
        var type = typeof(EventSubWebsocketClient);
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            Console.WriteLine($"  {field.Name}: {field.FieldType}");
        }
    }
}
