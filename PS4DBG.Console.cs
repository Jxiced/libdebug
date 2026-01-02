using System.Text;
using System.Threading.Tasks;

namespace libdebug {

    public partial class PS4DBG {

        //console
        // packet sizes
        // send size
        private const int CMD_CONSOLE_PRINT_PACKET_SIZE = 4;

        private const int CMD_CONSOLE_NOTIFY_PACKET_SIZE = 8;

        // console
        // note: the disconnect command actually uses the console api to end the connection
        /// <summary>
        /// Reboot console
        /// </summary>
        public async Task Reboot() {
            CheckConnected();

            await SendCMDPacket(CMDS.CMD_CONSOLE_REBOOT, 0);
            IsConnected = false;
        }

        /// <summary>
        /// Print to serial port
        /// </summary>
        public async Task Print(string str) {
            CheckConnected();

            string raw = str + "\0";

            await SendCMDPacket(CMDS.CMD_CONSOLE_PRINT, CMD_CONSOLE_PRINT_PACKET_SIZE, raw.Length);
            await SendDataAsync(Encoding.ASCII.GetBytes(raw), raw.Length);
            await CheckStatus();
        }

        /// <summary>
        /// Notify console
        /// </summary>
        public async Task Notify(int messageType, string message) {
            CheckConnected();

            string raw = message + "\0";

            await SendCMDPacket(CMDS.CMD_CONSOLE_NOTIFY, CMD_CONSOLE_NOTIFY_PACKET_SIZE, messageType, raw.Length);
            await SendDataAsync(Encoding.ASCII.GetBytes(raw), raw.Length);
            await CheckStatus();
        }

        /// <summary>
        /// Console information
        /// </summary>
        public async Task GetConsoleInformation() {
            CheckConnected();

            await SendCMDPacket(CMDS.CMD_CONSOLE_INFO, 0);
            await CheckStatus();

            // TODO return the data
        }
    }
}