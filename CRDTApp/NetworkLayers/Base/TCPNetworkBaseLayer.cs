using CRDTApp.Messages;
using CRDTApp.Schedulers;
using Shared;
using Simulator;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CRDTApp.NetworkLayers.Base
{
    public class TCPNetworkBaseLayer : NetworkLayer
    {
        const int MESSAGE_SIZE_LIMIT = 2048;

        readonly ConcurrentDictionary<ReplicaID, TcpClient> outputSockets = new();

        readonly ConcurrentDictionary<ReplicaID, SemaphoreSlim> sendLocks = new();

        readonly TcpListener listener;

        readonly Dictionary<string, ReplicaID> listenerPortMap;
        readonly Dictionary<ReplicaID, (IPAddress, int)> addressMap;

        IScheduler scheduler;
        public TCPNetworkBaseLayer(IScheduler scheduler, IsRunningTracker runningTracker, Dictionary<string, ReplicaID> listenerPortMap, Dictionary<ReplicaID, (IPAddress, int)> addressMap, IPAddress localAddress, int port):base("", runningTracker)
        {
            this.scheduler = scheduler; 
            this.listenerPortMap = new Dictionary<string, ReplicaID>(listenerPortMap);
            this.addressMap = new Dictionary<ReplicaID, (IPAddress, int)>(addressMap);
            listener = new TcpListener(localAddress, port);
        }

        async Task RunInputSocket(ReplicaID id, Socket socket)
        {
            using (socket)
            {
                var buffer = new byte[MESSAGE_SIZE_LIMIT];
                while (RunningTracker.IsRunning)
                {
                    try
                    {
                        int numBytes = await socket.ReceiveAsync(buffer);

                        if (numBytes == 0)
                        {
                            //connection interrupted
                            break;
                        }

                        var message = Encoding.UTF8.GetString(buffer, 0, numBytes);

                        var messageObj = Message.Decode(message);
                        CallMessageReceived(id, messageObj);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        // connection Interrupted
                        break;
                    }
                }
            }
        }



        int POLLTIME = 10000;
        async Task Run() {
            while (RunningTracker.IsRunning)
            {
                try
                {
                    var socket = await listener.AcceptSocketAsync();
                    var endpoint = socket.RemoteEndPoint?.ToString();
                    if (endpoint != null && listenerPortMap.TryGetValue(endpoint, out var replicaID))
                    {
                        var _ = Task.Run( async ()=> { await RunInputSocket(replicaID, socket); });
                    }
                    else
                    {
                        socket.Dispose(); // unknown peer
                    }


                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }

    

        public override void Launch()
        {
            Task.Run(Run);
        }

        public override async Task<bool> SendMessage(ReplicaID recipient, Message message)
        {
            if (!RunningTracker.IsRunning)
            {
                return false;
            }

            if (!addressMap.TryGetValue(recipient, out var address))
            {
                return false;
            }

            var sendLock = sendLocks.GetOrAdd(recipient, _ => new SemaphoreSlim(1, 1));

            await sendLock.WaitAsync();
            try
            {
                if (!outputSockets.TryGetValue(recipient, out var client))
                {
                    client = new TcpClient();
                    try
                    {
                        await client.ConnectAsync(address.Item1, address.Item2);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        client.Dispose();
                        return false;
                    }
                    outputSockets[recipient] = client;
                }

                try
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(message.Payload);
                    await client.GetStream().WriteAsync(bytes);
                    return true;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    outputSockets.TryRemove(recipient, out _);
                    client.Dispose();
                    return false;
                }
            }
            finally
            {
                sendLock.Release();
            }
        }
    }
}