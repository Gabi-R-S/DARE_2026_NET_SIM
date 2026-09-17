using CRDTApp.Messages;
using Shared;
using Simulator;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

//TODO: MAKE THREAD-SAFE!!!!
namespace CRDTApp.NetworkManagers
{
    public class TCPNetworkBaseLayer: NetworkLayer
    {
        const int MESSAGE_SIZE_LIMIT = 2048;
        Dictionary<ReplicaID, TcpClient> outputSockets = new(); //this client class is thread safe, supposedly
        TcpListener listener;
        Dictionary<string, ReplicaID> listenerPortMap;
        Dictionary<ReplicaID, (IPAddress, int)> addressMap;



        public TCPNetworkBaseLayer(Dictionary<string, ReplicaID> listenerPortMap, Dictionary< ReplicaID, (IPAddress, int)> addressMap, IPAddress localAddress, int port) { 
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
                    Console.WriteLine(e);
                    CallConnectionInterrupted(id);
                    break;
                }
            }

        }
        public override async Task Run() { 
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

        public override async Task<bool> SendMessage(ReplicaID recipient, Message message) {
            TcpClient client;
            if (!outputSockets.ContainsKey(recipient))
            {
                if (!addressMap.ContainsKey(recipient)) {
                    return false;
                }
                client = new TcpClient();
                try
                {
                    (IPAddress ip, int port) = addressMap[recipient];
                    await client.ConnectAsync(ip, port);

                    outputSockets[recipient] = client;
                }
                catch (Exception e) {
                    Console.WriteLine(e);
                    return false;
                }
            }
            else {

                client = outputSockets[recipient];
            }

            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(message.Payload);
                await client.GetStream().WriteAsync(bytes);
                return true;
            }
            catch(Exception e)
            {
                Console.WriteLine(e);
                outputSockets.Remove(recipient);
                return false;
            }
        }

       

    }
}
