using System;
using System.Threading.Tasks;

namespace libdebug {

    public partial class PS4DBG {

        // kernel
        //packet sizes
        //send size
        private const int CMD_KERN_READ_PACKET_SIZE = 12;

        private const int CMD_KERN_WRITE_PACKET_SIZE = 12;

        //receive size
        private const int KERN_BASE_SIZE = 8;

        /// <summary>
        /// Get kernel base address
        /// </summary>
        /// <returns></returns>
        public async Task<ulong> KernelBase() {
            CheckConnected();

            await SendCMDPacket(CMDS.CMD_KERN_BASE, 0);
            await CheckStatus();
            return BitConverter.ToUInt64(await ReceiveDataAsync(KERN_BASE_SIZE), 0);
        }

        /// <summary>
        /// Read memory from kernel
        /// </summary>
        /// <param name="address">Memory address</param>
        /// <param name="length">Data length</param>
        /// <returns></returns>
        public async Task<byte[]> KernelReadMemory(ulong address, int length) {
            CheckConnected();

            await SendCMDPacket(CMDS.CMD_KERN_READ, CMD_KERN_READ_PACKET_SIZE, address, length);
            await CheckStatus();
            return await ReceiveDataAsync(length);
        }

        /// <summary>
        /// Write memory in kernel
        /// </summary>
        /// <param name="address">Memory address</param>
        /// <param name="data">Data</param>
        public async Task KernelWriteMemory(ulong address, byte[] data) {
            CheckConnected();

            await SendCMDPacket(CMDS.CMD_KERN_WRITE, CMD_KERN_WRITE_PACKET_SIZE, address, data.Length);
            await CheckStatus();
            await SendDataAsync(data, data.Length);
            await CheckStatus();
        }
    }
}