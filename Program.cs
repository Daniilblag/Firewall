using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NdisApiDotNet;
using NdisApiDotNetPacketDotNet.Extensions;
using PacketDotNet;
using System.Net;

namespace ConsoleApp2
{
    class Program
    {
        private static void Main()
        {
            var filter = NdisApi.Open();
            if (!filter.IsValid)
                throw new ApplicationException("Драйвер не найден");

            Console.WriteLine($"Версия драйвера: {filter.GetVersion()}");

            // Создать и установить событие для адаптеров
            var waitHandlesCollection = new List<ManualResetEvent>();
            var tcpAdapters = new List<NetworkAdapter>();
            foreach (var networkAdapter in filter.GetNetworkAdapters())
            {
                if (networkAdapter.IsValid)
                {
                    var success = filter.SetAdapterMode(networkAdapter,
                                                            NdisApiDotNet.Native.NdisApi.MSTCP_FLAGS.MSTCP_FLAG_TUNNEL |
                                                            NdisApiDotNet.Native.NdisApi.MSTCP_FLAGS.MSTCP_FLAG_LOOPBACK_FILTER |
                                                            NdisApiDotNet.Native.NdisApi.MSTCP_FLAGS.MSTCP_FLAG_LOOPBACK_BLOCK);

                    var manualResetEvent = new ManualResetEvent(false);

                    success &= filter.SetPacketEvent(networkAdapter, manualResetEvent.SafeWaitHandle);

                    if (success)
                    {
                        Console.WriteLine($"Добавлен адаптер: {networkAdapter.FriendlyName}");
                        // Добавление адаптеров в список
                        waitHandlesCollection.Add(manualResetEvent);
                        tcpAdapters.Add(networkAdapter);
                    }
                }
            }

            var waitHandlesManualResetEvents = waitHandlesCollection.Cast<ManualResetEvent>().ToArray();
            var waitHandles = waitHandlesCollection.Cast<WaitHandle>().ToArray();
            // Запуск отдельного потока для анализа пакетов
            var t1 = Task.Factory.StartNew(() => PassThruThread(filter, waitHandles, tcpAdapters.ToArray(), waitHandlesManualResetEvents));
            Task.WaitAll(t1);

            Console.Read();
        }

        /// <summary>
        /// Starts a pass thru thread.
        /// </summary>
        /// <param name="filter">The filter.</param>
        /// <param name="waitHandles">The wait handles.</param>
        /// <param name="networkAdapters">The network adapters.</param>
        /// <param name="waitHandlesManualResetEvents">The wait handles manual reset events.</param>

        //Функция фильтрации пакетов
        private static void PassThruThread(NdisApi filter, WaitHandle[] waitHandles, IReadOnlyList<NetworkAdapter> networkAdapters, IReadOnlyList<ManualResetEvent> waitHandlesManualResetEvents)
        {
            var ndisApiHelper = new NdisApiHelper();
            var ethRequest = ndisApiHelper.CreateEthRequest();
            while (true)
            {
                var handle = WaitHandle.WaitAny(waitHandles);
                ethRequest.AdapterHandle = networkAdapters[handle].Handle;
                while (filter.ReadPacket(ref ethRequest))
                {
                    var ethPacket = ethRequest.Packet.GetEthernetPacket(ndisApiHelper);
                    if (ethPacket.PayloadPacket is IPv4Packet iPv4Packet)
                    {
                        if (iPv4Packet.PayloadPacket is TcpPacket tcpPacket)
                        {
                            Console.WriteLine($"{iPv4Packet.SourceAddress}:{tcpPacket.SourcePort}-> { iPv4Packet.DestinationAddress}:{ tcpPacket.DestinationPort}.");
                            // Фильтрация пакетов по 80 порту
                            if (tcpPacket.DestinationPort == 80)
                            {
                                continue;
                            }
                            // Фильтрация пакетов по IP адресу 127.0.0.1
                            if (iPv4Packet.DestinationAddress == IPAddress.Parse("127.0.0.1"))
                            {
                                continue;

                            }
                        }
                    }
                    filter.SendPacket(ref ethRequest);
                }
                waitHandlesManualResetEvents[handle].Reset();
            }
        }
    }
}
