using CRDTApp.Messages;
using Shared;
using Simulator;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

//TODO: THESE LOCKS MAY BE UNSAFE WHEN COMBINED WITH ASYNC
namespace CRDTApp.NetworkManagers
{
    public class TCPNetworkManager: INetworkManager
    {
        const int MESSAGE_SIZE_LIMIT = 2048;
        Dictionary<ReplicaID, TcpClient> outputSockets = new(); //this client class is thread safe, supposedly
        TcpListener listener;
        Dictionary<string, ReplicaID> listenerPortMap;
        Dictionary<ReplicaID, (IPAddress, int)> addressMap;




        object receiveLock = new object();
        object interruptedLock = new object();
        event Action<ReplicaID, Message?>? MessageReceived;
        event Action<ReplicaID>? ConnectionInterrupted;

        void CallMessageReceived(ReplicaID replica, Message? message) {
            lock (receiveLock)
            {
                MessageReceived?.Invoke(replica, message);
            }
        }

        void CallConnectionInterrupted(ReplicaID replica) {
            lock (interruptedLock)
            {
                ConnectionInterrupted?.Invoke(replica);
            }
        }

        public void AddOnMessageReceivedHandler(Action<ReplicaID, Message> handler)
        {
            lock (receiveLock)
            {
                MessageReceived += handler;
            }
        }

        public void RemoveOnMessageReceivedHandler(Action<ReplicaID, Message> handler)
        {
            lock (receiveLock)
            {
                MessageReceived -= handler;
            }
        }

        public void AddOnConnectionInterruptedHandler(Action<ReplicaID> handler)
        {
            lock (interruptedLock)
            {
                ConnectionInterrupted += handler;
            }
        }
        public void RemoveOnConnectionInterruptedHandler(Action<ReplicaID> handler)
        {
            lock (interruptedLock)
            {
                ConnectionInterrupted -= handler;
            }
        }

        public TCPNetworkManager(Dictionary<string, ReplicaID> listenerPortMap, Dictionary< ReplicaID, (IPAddress, int)> addressMap, IPAddress localAddress, int port) { 
            this.listenerPortMap = listenerPortMap;
            listener = new TcpListener(localAddress,port);
            this.addressMap = addressMap;
        }

        async Task RunInputSocket(ReplicaID id, Socket socket) {
            while (true)
            {
                try
                {
                    var buffer = new byte[MESSAGE_SIZE_LIMIT];
                    int numBytes = await socket.ReceiveAsync(buffer);

                    var message = Encoding.UTF8.GetString(buffer, 0, numBytes);

                    var messageObj = new Message(message);
                    CallMessageReceived(id, messageObj);
                }
                catch (Exception e) {
                    CallConnectionInterrupted(id);
                    break;
                }
            }

        }
        public async Task Run() { 
            var tasks = new List<Task>();
            while (true) 
            {
                try
                {
                    var socket = await listener.AcceptSocketAsync();
                    var endpoint = socket.RemoteEndPoint?.ToString();
                    if (endpoint != null)
                    {
                        if (listenerPortMap.ContainsKey(endpoint))
                        {
                            var replicaID = listenerPortMap[endpoint];
                            tasks.Add(RunInputSocket(replicaID, socket));

                        }
                    }
                    for (int i = 0; i < tasks.Count; i++) {
                        if (tasks[i].IsCompleted) {
                            i--;
                            tasks.RemoveAt(i);
                        }
                    }
                }
                catch (Exception e) {
                    Console.WriteLine(e);
                    break;
                }
            }

            Task.WaitAll(tasks);

        }

        async Task ConnectAndSendMessage(TcpClient client, ReplicaID recipient,Message message) {
            try
            {
                (IPAddress ip, int port) = addressMap[recipient];
                await client.ConnectAsync(ip, port);

                byte[] bytes = Encoding.UTF8.GetBytes(message.Payload);
                await client.GetStream().WriteAsync(bytes);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                CallConnectionInterrupted(recipient);
            }
        }
        async Task SendMessage(TcpClient client,ReplicaID recipient, Message message) {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(message.Payload);
                await client.GetStream().WriteAsync(bytes);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                CallConnectionInterrupted(recipient);
            }
        }

        public bool SendMessage(ReplicaID recipient, Message message) {
            TcpClient client;
            if (!outputSockets.ContainsKey(recipient))
            {
                if (!addressMap.ContainsKey(recipient)) {
                    return false;
                }
                client = new TcpClient();

                var t = ConnectAndSendMessage(client, recipient,message);
            }
            else { 
                client = outputSockets[recipient];
                var t = SendMessage(client,recipient, message);
            }

            
            return true;
        }

       

    }
}
