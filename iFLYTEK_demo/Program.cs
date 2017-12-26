using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Threading;


namespace iFLYTEK_demo
{
    class Program
    {
        static void Main(string[] args)
        {

            string lgi_param = "appid = 5a3d0787";
            string ise_ssb_param = "sub=ise,category=read_sentence,language=en_us,aue=speex-wb;7,auf=audio/L16;rate=16000";

            int a = ISEDLL.MSPLogin(null, null, lgi_param);


            var re = ISEDLL.QISESessionBegin(ise_ssb_param, null, ref a);
            string senssionid = UnmanagedManager.GetStringFromUnmanagedMemory(re);
            string text = "[content]\r\nIt was two weeks before the Spring Festival and the shopping centre was crowded with shoppers.\r\n";
            uint len = (uint)text.Length;
            int re2 = ISEDLL.QISETextPut(UnmanagedManager.GetStringFromUnmanagedMemory(re), text, len, null);
            int state = 0; int state2 = 0;


            string path = @"C:\Users\Administrator\Desktop\科大讯飞\bin\ise_en\en_sentence.wav";

            FileStream fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
            int size = (int)fs.Length;
            IntPtr h = UnmanagedManager.UnmanagedReader(fs, size);
            int pcmCount = 0;
            IntPtr hnow = h;
            int ret;
            int audStat = 2;

            while (true)
            {
                uint len2 = 6400;
                if (size <= 2 * len)
                {
                    len2 = (uint)size;
                }
                if (len2 <= 0)
                {
                    break;
                }
                if (0 == pcmCount)
                {
                    audStat = 1;
                }
                ret = ISEDLL.QISEAudioWrite(senssionid, h + (int)pcmCount, (uint)len2, audStat, ref state, ref state2);
                Console.WriteLine(state2);
                pcmCount += (int)len2;
                size -= (int)len2;
            }
            ret = ISEDLL.QISEAudioWrite(senssionid, h + pcmCount, (uint)0, 4, ref audStat, ref state2);
            uint left = 0;
            //   string result = "";

            while (state2 != 5)
            {
                IntPtr result = ISEDLL.QISEGetResult(senssionid, ref left, ref state2, ref ret);
                if (state2 == 5)
                    Console.Write(UnmanagedManager.GetStringFromUnmanagedMemory(result));
            }
            Console.Read();

        }
    }
    public class UnmanagedManager
    {
        /// <summary>
        /// 非托管读取文件
        /// </summary>
        /// <param name="fs">想写入非托管内存的文件流</param>
        /// <param name="size">文件流大小</param>
        /// <returns>返回指向文件内容的指针</returns>
        public static IntPtr UnmanagedReader(FileStream fs, int size)
        {
            IntPtr h = Marshal.AllocHGlobal(size);
            int ofs = 0;
            while (true)
            {
                int now = fs.ReadByte();
                if (now == -1)
                    break;
                Marshal.WriteByte(h, ofs, (byte)now);
                ofs++;
            }
            return h;
        }
        /// <summary>
        /// 指针转字符串
        /// </summary>
        /// <param name="p">指向非托管代码字符串的指针</param>
        /// <returns>返回指针指向的字符串</returns>
        public static string GetStringFromUnmanagedMemory(IntPtr p)
        {
            List<byte> lb = new List<byte>();
            while (Marshal.ReadByte(p) != 0)
            {
                lb.Add(Marshal.ReadByte(p));
                p = p + 1;
            }
            byte[] bs = lb.ToArray();
            return Encoding.Default.GetString(lb.ToArray());
        }
    }
    public class ISEDLL
    {
        #region ISE dll import
        [DllImport(@"msc.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern int MSPLogin(string usr, string pwd, string param);
        [DllImport(@"msc.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern IntPtr QISESessionBegin(string param, string userModelId, ref int errorCode);
        [DllImport(@"msc.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern int QISETextPut(string sessionID, string textString, uint textLen, string param);
        [DllImport(@"msc.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern int QISEAudioWrite(string sessionID, IntPtr waveDate, uint waveLen, int audioStatus, ref int epStatus, ref int Status);
        [DllImport(@"msc.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern IntPtr QISEGetResult(string sessionID, ref uint rsltLen, ref int rsltStatus, ref int errorCode);
        #endregion
    }
}
