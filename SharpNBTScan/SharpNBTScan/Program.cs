﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SharpNBTScan
{
    class Program
    {
        #region NetBIOS NameQuery
        static byte[] NameQuery =
        {
            0x00,0x00,0x00,0x00,0x00,0x01,0x00,0x00,
            0x00,0x00,0x00,0x00,0x20,0x43,0x4b,0x41,
            0x41,0x41,0x41,0x41,0x41,0x41,0x41,0x41,
            0x41,0x41,0x41,0x41,0x41,0x41,0x41,0x41,
            0x41,0x41,0x41,0x41,0x41,0x41,0x41,0x41,
            0x41,0x41,0x41,0x41,0x41,0x00,0x00,0x21,
            0x00,0x01
        };
        #endregion

        #region  UDP Client Config Params
        static bool IsUdpcRecvStart = false;
        static UdpClient UdpClient = null;
        static IPEndPoint RemoteIPEndPoint = null;
        #endregion

        public static void StartReceive()
        {
            RemoteIPEndPoint = new IPEndPoint(IPAddress.Any, 137);
            UdpState udpState = new UdpState(UdpClient, RemoteIPEndPoint);
            IsUdpcRecvStart = true;
            UdpClient.BeginReceive(RecieveMessage, udpState);
        }

        public static void NameQueryResponseResolver(byte[] NameQueryResponse, IPAddress address)
        {
            nb_host_info HostInfo = new nb_host_info();
            try
            {
                HostInfo = NBNSResolver.NBNSParser(NameQueryResponse, NameQueryResponse.Length);
            
                #region identify  groupname\computername and service
                string ComputerName = "";
                string GroupName = "";
                bool IsComputerName = true;
                char chrService = '\xff';
                string ServiceName = "";
                for (int i = 0; i < HostInfo.header.number_of_names; i++)
                {
                    chrService = HostInfo.names[i].ascii_name.AsQueryable().Last();
                    ushort flag = HostInfo.names[i].rr_flags;
                    if (chrService == '\x00' && IsComputerName && ((flag & 0x8000) == 0))
                    {
                        ComputerName = new string(HostInfo.names[i].ascii_name).Replace('\0', ' ').Trim();
                        IsComputerName = false;
                    }
                    else if (chrService == '\x00')
                    {
                        GroupName = new string(HostInfo.names[i].ascii_name).Replace('\0', ' ').Trim();
                    }
                    else if (chrService == '\x1C')
                    {
                        ServiceName = "DC";
                    }

                }
                #endregion

                #region identify device via mac
                String Device = "";
                Device = NBNSResolver.MACParser(HostInfo.footer.adapter_address);
                #endregion

                Console.WriteLine(String.Format("{0,-15}", address) + String.Format("{0,-30}", GroupName + '\\' + ComputerName)
                    + String.Format("{0,-10}", ServiceName) + String.Format("{0,-15}", Device));
            }
            catch (Exception e)
            {
                Console.WriteLine(String.Format("{0,-15}", address) + e.Message);
            }
        }

        public static void RecieveMessage(IAsyncResult asyncResult)
        {
            UdpState udpState = (UdpState)asyncResult.AsyncState;
            if (udpState != null)
            {
                UdpClient udpClient = udpState.UdpClient;
                IPEndPoint iPEndPoint = udpState.IP;
                if (IsUdpcRecvStart)
                {
                    byte[] NameQueryResponse = udpClient.EndReceive(asyncResult, ref iPEndPoint);
                    udpClient.BeginReceive(RecieveMessage, udpState);
                    if (NameQueryResponse.Length != 0)
                    {
                        NameQueryResponseResolver(NameQueryResponse, iPEndPoint.Address);
                    }
                }
            }
        }

        public class UdpState
        {
            private UdpClient udpclient = null;
            public UdpClient UdpClient
            {
                get { return udpclient; }
            }
            private IPEndPoint ip;
            public IPEndPoint IP
            {
                get { return ip; }
            }
            public UdpState(UdpClient udpclient, IPEndPoint ip)
            {
                this.udpclient = udpclient;
                this.ip = ip;
            }
        }

        #region cidr parser
        private static List<string> Network2IpRange(string sNetwork)
        {
            string[] iparray = new string[0];
            List<string> iparrays = iparray.ToList();
            uint ip,        /* ip address */
            mask,       /* subnet mask */
            broadcast,  /* Broadcast address */
            network;    /* Network address */
            int bits;
            string[] elements = sNetwork.Split(new Char[] { '/' });
            if (elements.Length == 1) { iparrays.Add(sNetwork); return iparrays; }
            ip = IP2Int(elements[0]);
            bits = Convert.ToInt32(elements[1]);
            mask = ~(0xffffffff >> bits);
            network = ip & mask;
            broadcast = network + ~mask;
            uint usableIps = (bits > 30) ? 0 : (broadcast - network - 1);
            Console.WriteLine("[+] ip range {0} - {1} ", Int2IP(network + 1), Int2IP(broadcast - 1));
            for (uint i = 1; i < usableIps + 1; i++)
            {
                iparrays.Add(Int2IP(network + i));
            }
            return iparrays;
        }

        public static uint IP2Int(string IPNumber)
        {
            uint ip = 0;
            string[] elements = IPNumber.Split(new Char[] { '.' });
            if (elements.Length == 4)
            {
                ip = Convert.ToUInt32(elements[0]) << 24;
                ip += Convert.ToUInt32(elements[1]) << 16;
                ip += Convert.ToUInt32(elements[2]) << 8;
                ip += Convert.ToUInt32(elements[3]);
            }
            return ip;
        }

        public static string Int2IP(uint ipInt)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append((ipInt >> 24) & 0xFF).Append(".");
            sb.Append((ipInt >> 16) & 0xFF).Append(".");
            sb.Append((ipInt >> 8) & 0xFF).Append(".");
            sb.Append(ipInt & 0xFF);
            return sb.ToString();
        }
        #endregion

        static void Main(string[] args)
        {
            try
            {
                if (args.Length != 1)
                {
                    Console.WriteLine("[-]usage: SharpNBTScan.exe TargetIp (e.g.: SharpNBTScan.exe 192.168.0.1/24)");
                    return;
                }
                string sNetwork = args[0];
                UdpClient = new UdpClient(0);
                uint IOC_IN = 0x80000000;
                uint IOC_VENDOR = 0x18000000;
                uint SIO_UDP_CONNRESET = IOC_IN | IOC_VENDOR | 12;
                UdpClient.Client.IOControl((int)SIO_UDP_CONNRESET, new byte[] { Convert.ToByte(false) }, null);
                Console.WriteLine("[*]Start udp client ...");
                StartReceive();
                List<string> ips = Network2IpRange(sNetwork);
                foreach (string ip in ips)
                {
                    IPEndPoint remoteIPEndPoint = new IPEndPoint(IPAddress.Parse(ip), 137);
                    UdpClient.Send(NameQuery, NameQuery.Length, remoteIPEndPoint);
                }
                Console.WriteLine("[+]Udp client will stop in 10 s ...");
                Thread.Sleep(10000);
                Console.WriteLine("[*]Stop udp client ...");
                IsUdpcRecvStart = false;
                UdpClient.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[!]Error: {0}", ex.Message);
            }
        }
    }
}
