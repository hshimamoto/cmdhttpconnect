// MIT License Copyright(c) 2022 Hiroshi Shimamoto
// vim: set sw=4 sts=4:
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace HttpConnect
{
    class Server
    {
	private readonly string proxy;
	private readonly string target;
	private readonly TcpListener listener;
	public Server(string p, string t, int listen)
	{
	    proxy = p;
	    target = t;
	    // create listener
	    //listener = new TcpListener(IPAddress.Any, listen);
	    listener = new TcpListener(IPAddress.Loopback, listen);
	}
	// CopyStream
	private void CopyStream(NetworkStream src, NetworkStream dst)
	{
	    byte[] buf = new byte[4096];
	    while (true) {
		try {
		    int n = src.Read(buf, 0, 4096);
		    if (n <= 0) {
			Console.WriteLine("src closed");
			return;
		    }
		    dst.Write(buf, 0, n);
		} catch (Exception e) {
		    // close or error
		    Console.WriteLine($"CopyStream {e}");
		    return;
		}
	    }
	}
	// Relay Task
	public async Task<int> RelayTask(TcpClient cli)
	{
	    Console.WriteLine("Relay Task START");
	    // first, try to connect through proxy
	    try {
		var proxy_a = proxy.Split(":");
		var addrs = Dns.GetHostAddresses(proxy_a[0]);
		if (addrs.Length == 0) {
		    Console.WriteLine($"Bad proxy: {proxy}");
		    return 0;
		}
		var port = int.Parse(proxy_a[1]);
		var ep = new IPEndPoint(addrs[0], port);

		Console.WriteLine($"Trying to connect {addrs[0]} {port}");
		var sock = new TcpClient();

		await sock.ConnectAsync(ep.Address, ep.Port);
		Console.WriteLine($"connected to proxy {addrs[0]} {port}");

		using (NetworkStream stream = sock.GetStream()) {
		    // create CONNECT request
		    string req = $"CONNECT {target} HTTP/1.1\r\n\r\n";

		    var breq = Encoding.ASCII.GetBytes(req);
		    await stream.WriteAsync(breq, 0, breq.Length);

		    byte[] buf = new byte[256];
		    int r = await stream.ReadAsync(buf, 0, buf.Length);
		    if (r <= 0) {
			// closed?
			return 0;
		    }
		    // wait HTTP/1.1 200 OK
		    string resp = Encoding.ASCII.GetString(buf);
		    var resp_a = resp.Split(" ");
		    if (resp_a.Length < 3) {
			// bad resp
			return 0;
		    }
		    Console.WriteLine($"proxy response code={resp_a[1]}");
		    if (resp_a[1] != "200") {
			// bad resp
			return 0;
		    }
		    // okay CONNECTED
		    Console.WriteLine($"CONNECTED to {target}");

		    using (NetworkStream lstream = cli.GetStream()) {
			// start local->remote Task
			var lr_task = Task.Run(() => CopyStream(lstream, stream));
			var rl_task = Task.Run(() => CopyStream(stream, lstream));
			// finally wait tasks
			lr_task.Wait();
			rl_task.Wait();
			Console.WriteLine("Copy tasks DONE");
		    }
		}
	    } catch (Exception e) {
		Console.WriteLine($"Relay Exception {e}");
	    }
	    Console.WriteLine("Relay Task DONE");
	    return 0;
	}
	public async Task Listen()
	{
	    Console.WriteLine("Listening");
	    listener.Start();
	    while (true) {
		var cli = await listener.AcceptTcpClientAsync();
		Console.WriteLine("Accepted");
		// create task
		var task = Task.Run(() => RelayTask(cli));
		// no care about task
	    }
	}
    }
    class Program
    {
	static void Usage()
	{
	    Console.WriteLine("httpconnect.exe <proxy hostname:port> <target hostname:port> <listen port>");
	}
	static void Main(string[] args)
	{
	    if (args.Length < 3) {
		Usage();
		return;
	    }
	    string proxy = args[0];
	    string target = args[1];
	    string listen = args[2];

	    string[] proxy_a = proxy.Split(':');
	    string[] target_a = target.Split(':');

	    if (proxy_a.Length != 2) {
		Console.WriteLine($"Bad proxy: {proxy}");
		return;
	    }
	    if (target_a.Length != 2) {
		Console.WriteLine($"Bad target: {target}");
		return;
	    }

	    var serv = new Server(proxy, target, int.Parse(listen));
	    // server run
	    serv.Listen().Wait();
	}
    }
}
