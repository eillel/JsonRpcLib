# JsonRpcLib
C# DotNetCore 2.1+ Client/Server Json RPC library
<br/>
Using Span&lt;T&gt;, Memory&lt;T&gt; and IO pipelines

[![NuGet version (Bundgaard.JsonRpcLib)](https://img.shields.io/nuget/v/Bundgaard.JsonRpcLib.svg)](https://www.nuget.org/packages/Bundgaard.JsonRpcLib/)
[![license](https://img.shields.io/github/license/jbdk/JsonRpcLib.svg)](LICENSE.md)
[![Build Status](https://travis-ci.org/jbdk/JsonRpcLib.svg?branch=master)](https://travis-ci.org/jbdk/JsonRpcLib)
[![Build status](https://ci.appveyor.com/api/projects/status/526taqgoctsoa26a/branch/master?svg=true)](https://ci.appveyor.com/project/jbdk/jsonrpclib/branch/master)


### Current performance 
Run the PerfTest app
 - 8 threads 1,000,000 json notify -> static class call: `~1,500,000 requests/sec`
 - 8 threads 100,000 json invoke -> static class call: `~27,500 requests/sec` 

Test machine: 3.4 Ghz i5 3570

# The Server
JsonRpc server using SocketListener class (corefxlab)
````csharp
public class MyServer : JsonRpcServer
{
    readonly SocketListener _listener;
    TaskCompletionSource<int> _tcs = new TaskCompletionSource<int>();

    public MyServer(int port)
    {
        _listener = new SocketListener();
        _listener.Start(new IPEndPoint(IPAddress.Any, port));
        _listener.OnConnection(OnConnection);
    }

    private Task OnConnection(SocketConnection connection)
    {
        IClient client = AttachClient(connection.GetRemoteIp(), connection);
        return _tcs.Task;
    }

    public override void Dispose()
    {
        _tcs.TrySetCanceled();
        _listener?.Stop();
        base.Dispose();
    }
}
````

Start the server and register methods

````csharp
const int port = 7733;
using(var server = new MyServer(port))
{
    // Bind to functions on static class
    server.Bind(typeof(Target));    

    // Bind to a delegate
    server.Bind("DelegateMethod", (Action<int,int>)( (a, b)
        => Debug.WriteLine($"DelegateMethod. a={a} b={b}") ));
}

static class Target
{
    public static int TestMethod()
    {
        Debug.WriteLine("TestMethod called");
        return 42;
    }
}

````
You need this little extension to get the clients IP address on the server
````csharp
public static class SocketExtensions
{
    static readonly PropertyInfo s_socketProperty;
    static SocketExtensions()
    {
        s_socketProperty = typeof(SocketConnection).GetProperty("Socket",
            BindingFlags.NonPublic | BindingFlags.Instance);
    }

    public static string GetRemoteIp(this SocketConnection conn)
    {
        if (s_socketProperty?.GetValue(conn) is Socket socket)
        {
            var ipEP = socket.RemoteEndPoint as IPEndPoint;
            return ipEP.Address.MapToIPv4().ToString();
        }
        return null;
    }
}
````

# The Client
JsonRpc client using SocketConnection class (corefxlab)
````csharp
public class MyClient
{
    private readonly SocketConnection _conn;

    public static async Task<JsonRpcClient> ConnectAsync(int port)
    {
        var c = await SocketConnection.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port));
        return new JsonRpcClient(c);
    }
}
````

Connect client to server and call the methods
````csharp
const int port = 7733;
using(var client = await MyClient.ConnectAsync(port))
{
    var result = await client.InvokeAsync<int>("TestMethod");
    await client.InvokeAsync("DelegateMethod", 44, 76);

    // Fire-and-forget 
    client.Notify("DelegateMethod", 2, 6);
}
````
