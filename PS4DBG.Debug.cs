using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace libdebug
{
    public partial class PS4DBG
    {
        //debug
        // packet sizes
        //send size
        private const int CMD_DEBUG_ATTACH_PACKET_SIZE = 4;

        private const int CMD_DEBUG_BREAKPT_PACKET_SIZE = 16;
        private const int CMD_DEBUG_WATCHPT_PACKET_SIZE = 24;
        private const int CMD_DEBUG_STOPTHR_PACKET_SIZE = 4;
        private const int CMD_DEBUG_RESUMETHR_PACKET_SIZE = 4;
        private const int CMD_DEBUG_GETREGS_PACKET_SIZE = 4;
        private const int CMD_DEBUG_SETREGS_PACKET_SIZE = 8;
        private const int CMD_DEBUG_STOPGO_PACKET_SIZE = 4;
        private const int CMD_DEBUG_THRINFO_PACKET_SIZE = 4;

        private const int CMD_DEBUG_EXT_STOPGO_PACKET_SIZE = 5;

        //receive size
        private const int DEBUG_INTERRUPT_SIZE = 0x4A0;

        private const int DEBUG_THRINFO_SIZE = 40;
        private const int DEBUG_REGS_SIZE = 0xB0;
        private const int DEBUG_FPREGS_SIZE = 0x340;
        private const int DEBUG_DBGREGS_SIZE = 0x80;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct DebuggerInterruptPacket {
            public uint lwpid;
            public uint status;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 40)]
            public string tdname;

            public regs reg64;
            public fpregs savefpu;
            public dbregs dbreg64;
        }

        /// <summary>
        /// Debugger interrupt callback
        /// </summary>
        /// <param name="lwpid">Thread identifier</param>
        /// <param name="status">status</param>
        /// <param name="tdname">Thread name</param>
        /// <param name="regs">Registers</param>
        /// <param name="fpregs">Floating point registers</param>
        /// <param name="dbregs">Debug registers</param>
        public delegate void DebuggerInterruptCallback(uint lwpid, uint status, string tdname, regs regs, fpregs fpregs, dbregs dbregs);

        private async Task DebuggerThread(object obj) {
            DebuggerInterruptCallback callback = (DebuggerInterruptCallback)obj;

            IPAddress ip = IPAddress.Parse("0.0.0.0");
            IPEndPoint endpoint = new IPEndPoint(ip, PS4DBG_DEBUG_PORT);

            Socket server = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            server.Bind(endpoint);
            server.Listen(0);

            IsDebugging = true;

            Socket cl = await server.AcceptAsync();

            cl.NoDelay = true;
            cl.Blocking = false;

            while (IsDebugging) {
                if (cl.Available == DEBUG_INTERRUPT_SIZE) {
                    byte[] data = new byte[DEBUG_INTERRUPT_SIZE];
                    int bytes = await cl.ReceiveAsync(data, SocketFlags.None);
                    if (bytes == DEBUG_INTERRUPT_SIZE) {
                        DebuggerInterruptPacket packet = (DebuggerInterruptPacket)GetObjectFromBytes(data, typeof(DebuggerInterruptPacket));
                        callback(packet.lwpid, packet.status, packet.tdname, packet.reg64, packet.savefpu, packet.dbreg64);
                    }
                }

                await Task.Delay(100);
            }

            server.Close();
        }

        /// <summary>
        /// Attach the debugger
        /// </summary>
        /// <param name="pid">Process ID</param>
        /// <param name="callback">DebuggerInterruptCallback implementation</param>
        /// <returns></returns>
        public async Task AttachDebugger(int pid, DebuggerInterruptCallback callback) {
            CheckConnected();

            if (IsDebugging || debugThread != null) {
                throw new Exception("libdbg: debugger already running?");
            }

            IsDebugging = false;

            debugThread = Task.Run(() => DebuggerThread(callback));

            // wait until server is started
            while (!IsDebugging) {
                await Task.Delay(100);
            }

            await SendCMDPacket(CMDS.CMD_DEBUG_ATTACH, CMD_DEBUG_ATTACH_PACKET_SIZE, pid);
            await CheckStatus();
        }

        /// <summary>
        /// Detach the debugger
        /// </summary>
        /// <returns></returns>
        public async Task DetachDebugger() {
            CheckConnected();

            await SendCMDPacket(CMDS.CMD_DEBUG_DETACH, 0);
            await CheckStatus();

            if (IsDebugging && debugThread != null) {
                IsDebugging = false;

                await debugThread;

                debugThread = null;
            }
        }

        /// <summary>
        /// Stop the current process
        /// </summary>
        /// <returns></returns>
        public async Task ProcessStop() {
            CheckConnected();
            CheckDebugging();

            await SendCMDPacket(CMDS.CMD_DEBUG_STOPGO, CMD_DEBUG_STOPGO_PACKET_SIZE, 1);
            await CheckStatus();
        }

        /// <summary>
        /// Kill the current process, it will detach before doing so
        /// </summary>
        /// <returns></returns>
        public async Task ProcessKill() {
            CheckConnected();
            CheckDebugging();

            await SendCMDPacket(CMDS.CMD_DEBUG_STOPGO, CMD_DEBUG_STOPGO_PACKET_SIZE, 2);
            await CheckStatus();
        }

        /// <summary>
        /// Resume the current process
        /// </summary>
        /// <returns></returns>
        public async Task ProcessResume() {
            CheckConnected();
            CheckDebugging();
            
            await SendCMDPacket(CMDS.CMD_DEBUG_STOPGO, CMD_DEBUG_STOPGO_PACKET_SIZE, 0);
            await CheckStatus();
        }

        public async Task<int> GetExtFWVersion() {
            if (ExtFWVersion != 0)
                return ExtFWVersion;

            try
            {
                CheckConnected();

                await SendCMDPacket(CMDS.CMD_EXT_FW_VERSION, 0);
                int save = sock.ReceiveTimeout;
                sock.ReceiveTimeout = 10000;

                byte[] ldata = new byte[2];
                await sock.ReceiveAsync(ldata, SocketFlags.None);

                ExtFWVersion = BitConverter.ToUInt16(ldata, 0);
                Console.WriteLine("Console Version: " + ExtFWVersion);

                sock.ReceiveTimeout = save;

                return ExtFWVersion;
            } catch (Exception ex) {
                return 0;
            }
        }

        public async Task ProcessExtStop(int pid) {
            try {
                CheckConnected();

                await SendCMDPacket(CMDS.CMD_DEBUG_EXT_STOPGO, CMD_DEBUG_EXT_STOPGO_PACKET_SIZE, (uint)pid, (byte)1);
                await CheckStatus();
            } catch (Exception e) {
                Console.WriteLine(e);
            }
        }

        public async Task ProcessExtResume(int pid) {
            try {
                CheckConnected();

                await SendCMDPacket(CMDS.CMD_DEBUG_EXT_STOPGO, CMD_DEBUG_EXT_STOPGO_PACKET_SIZE, (uint)pid, (byte)0);
                await CheckStatus();
            } catch (Exception e) {
                Console.WriteLine(e);
            }
        }

        public async Task ProcessExtKill(int pid) {
            try {
                CheckConnected();

                await SendCMDPacket(CMDS.CMD_DEBUG_EXT_STOPGO, CMD_DEBUG_EXT_STOPGO_PACKET_SIZE, (uint)pid, (byte)2);
                await CheckStatus();
            } catch (Exception e) {
                Console.WriteLine(e);
            }
        }

        /// <summary>
        /// Change breakpoint, to remove said breakpoint send the same index but disable it (address is ignored)
        /// </summary>
        /// <param name="index">Index</param>
        /// <param name="enabled">Enabled</param>
        /// <param name="address">Address</param>
        /// <returns></returns>
        public async Task ChangeBreakpoint(int index, bool enabled, ulong address) {
            CheckConnected();
            CheckDebugging();

            if (index >= MAX_BREAKPOINTS) {
                throw new Exception("libdbg: breakpoint index out of range");
            }

            await SendCMDPacket(CMDS.CMD_DEBUG_BREAKPT, CMD_DEBUG_BREAKPT_PACKET_SIZE, index, Convert.ToInt32(enabled), address);
            await CheckStatus();
        }

        /// <summary>
        /// Change watchpoint
        /// </summary>
        /// <param name="index">Index</param>
        /// <param name="enabled">Enabled</param>
        /// <param name="length">Length</param>
        /// <param name="breaktype">Break type</param>
        /// <param name="address">Address</param>
        /// <returns></returns>
        public async Task ChangeWatchpoint(int index, bool enabled, WATCHPT_LENGTH length, WATCHPT_BREAKTYPE breaktype, ulong address) {
            CheckConnected();
            CheckDebugging();

            if (index >= MAX_WATCHPOINTS) {
                throw new Exception("libdbg: watchpoint index out of range");
            }

            await SendCMDPacket(CMDS.CMD_DEBUG_WATCHPT, CMD_DEBUG_WATCHPT_PACKET_SIZE, index, Convert.ToInt32(enabled), (uint)length, (uint)breaktype, address);
            await CheckStatus();
        }

        /// <summary>
        /// Get a list of threads from the current process
        /// </summary>
        /// <returns></returns>
        public async Task<uint[]> GetThreadList() {
            CheckConnected();
            CheckDebugging();

            await SendCMDPacket(CMDS.CMD_DEBUG_THREADS, 0);
            await CheckStatus();

            byte[] data = new byte[sizeof(int)];
            sock.Receive(data, sizeof(int), SocketFlags.None);
            int number = BitConverter.ToInt32(data, 0);

            byte[] threads = await ReceiveDataAsync(number * sizeof(uint));
            uint[] thrlist = new uint[number];
            for (int i = 0; i < number; i++) {
                thrlist[i] = BitConverter.ToUInt32(threads, i * sizeof(uint));
            }

            return thrlist;
        }

        /// <summary>
        /// Get thread information
        /// </summary>
        /// <returns></returns>
        /// <param name="lwpid">Thread identifier</param>
        public async Task<ThreadInfo> GetThreadInfo(uint lwpid) {
            CheckConnected();
            CheckDebugging();

            await SendCMDPacket(CMDS.CMD_DEBUG_THRINFO, CMD_DEBUG_THRINFO_PACKET_SIZE, lwpid);
            await CheckStatus();

            return (ThreadInfo)GetObjectFromBytes(await ReceiveDataAsync(DEBUG_THRINFO_SIZE), typeof(ThreadInfo));
        }

        /// <summary>
        /// Stop a thread from running
        /// </summary>
        /// <param name="lwpid">Thread id</param>
        /// <returns></returns>
        public async Task StopThread(uint lwpid) {
            CheckConnected();
            CheckDebugging();

            await SendCMDPacket(CMDS.CMD_DEBUG_STOPTHR, CMD_DEBUG_STOPTHR_PACKET_SIZE, lwpid);
            await CheckStatus();
        }

        /// <summary>
        /// Resume a thread from being stopped
        /// </summary>
        /// <param name="lwpid">Thread id</param>
        /// <returns></returns>
        public async Task ResumeThread(uint lwpid) {
            CheckConnected();
            CheckDebugging();

            await SendCMDPacket(CMDS.CMD_DEBUG_RESUMETHR, CMD_DEBUG_RESUMETHR_PACKET_SIZE, lwpid);
            await CheckStatus();
        }

        /// <summary>
        /// Get registers from thread
        /// </summary>
        /// <param name="lwpid">Thread id</param>
        /// <returns></returns>
        public async Task<regs> GetRegisters(uint lwpid) {
            CheckConnected();
            CheckDebugging();

            await SendCMDPacket(CMDS.CMD_DEBUG_GETREGS, CMD_DEBUG_GETREGS_PACKET_SIZE, lwpid);
            await CheckStatus();

            return (regs)GetObjectFromBytes(await ReceiveDataAsync(DEBUG_REGS_SIZE), typeof(regs));
        }

        /// <summary>
        /// Set thread registers
        /// </summary>
        /// <param name="lwpid">Thread id</param>
        /// <param name="regs">Register data</param>
        /// <returns></returns>
        public async Task SetRegisters(uint lwpid, regs regs) {
            CheckConnected();
            CheckDebugging();

            await SendCMDPacket(CMDS.CMD_DEBUG_SETREGS, CMD_DEBUG_SETREGS_PACKET_SIZE, lwpid, DEBUG_REGS_SIZE);
            await CheckStatus();
            await SendDataAsync(GetBytesFromObject(regs), DEBUG_REGS_SIZE);
            await CheckStatus();
        }

        /// <summary>
        /// Get floating point registers from thread
        /// </summary>
        /// <param name="lwpid">Thread id</param>
        /// <returns></returns>
        public async Task<fpregs> GetFloatRegisters(uint lwpid) {
            CheckConnected();
            CheckDebugging();

            await SendCMDPacket(CMDS.CMD_DEBUG_GETFPREGS, CMD_DEBUG_GETREGS_PACKET_SIZE, lwpid);
            await CheckStatus();

            return (fpregs)GetObjectFromBytes(await ReceiveDataAsync(DEBUG_FPREGS_SIZE), typeof(fpregs));
        }

        /// <summary>
        /// Set floating point thread registers
        /// </summary>
        /// <param name="lwpid">Thread id</param>
        /// <param name="fpregs">Floating point register data</param>
        /// <returns></returns>
        public async Task SetFloatRegisters(uint lwpid, fpregs fpregs) {
            CheckConnected();
            CheckDebugging();

            await SendCMDPacket(CMDS.CMD_DEBUG_SETFPREGS, CMD_DEBUG_SETREGS_PACKET_SIZE, lwpid, DEBUG_FPREGS_SIZE);
            await CheckStatus();
            await SendDataAsync(GetBytesFromObject(fpregs), DEBUG_FPREGS_SIZE);
            await CheckStatus();
        }

        /// <summary>
        /// Get debug registers from thread
        /// </summary>
        /// <param name="lwpid">Thread id</param>
        /// <returns></returns>
        public async Task<dbregs> GetDebugRegisters(uint lwpid) {
            CheckConnected();
            CheckDebugging();

            await SendCMDPacket(CMDS.CMD_DEBUG_GETDBGREGS, CMD_DEBUG_GETREGS_PACKET_SIZE, lwpid);
            await CheckStatus();

            return (dbregs)GetObjectFromBytes(await ReceiveDataAsync(DEBUG_DBGREGS_SIZE), typeof(dbregs));
        }

        /// <summary>
        /// Set debug thread registers
        /// </summary>
        /// <param name="lwpid">Thread id</param>
        /// <param name="dbregs">debug register data</param>
        /// <returns></returns>
        public async Task SetDebugRegisters(uint lwpid, dbregs dbregs) {
            CheckConnected();
            CheckDebugging();

            await SendCMDPacket(CMDS.CMD_DEBUG_SETDBGREGS, CMD_DEBUG_SETREGS_PACKET_SIZE, lwpid, DEBUG_DBGREGS_SIZE);
            await CheckStatus();
            await SendDataAsync(GetBytesFromObject(dbregs), DEBUG_DBGREGS_SIZE);
            await CheckStatus();
        }

        /// <summary>
        /// Executes a single instruction
        /// </summary>
        public async Task SingleStep() {
            CheckConnected();
            CheckDebugging();

            await SendCMDPacket(CMDS.CMD_DEBUG_SINGLESTEP, 0);
            await CheckStatus();
        }
    }
}