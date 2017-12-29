using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Xml;

namespace iFLYTEK_demo
{
    class Program
    {
        static void Main(string[] args)
        {
            using (ISEServerAgent a = new ISEServerAgent())
            {
                a.Login("your appid");
                a.TextPut("[content]\r\nIt was two weeks before the Spring Festival and the shopping centre was crowded with shoppers.\r\n");
                string path = @"C:\Users\Administrator\Desktop\科大讯飞\bin\ise_en\en_sentence.wav";
                FileStream fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
                a.AudioWrite(fs);
                ISEResultReader reader = new ISEResultReader(a.GetAnswer());
            }
            Console.Read();
        }
    }
    public class ISEResultReader
    {
        public const double PassScore = 3.0;
        private XmlElement xeSentence;
        public double TotalScore
        {
            get
            {
                return _totalScore;
            }
        }
        public string Content
        {
            get
            {
                return _content;
            }
        }
        public List<ISEWord> AnswerList;
        private string _content = null;
        private double _totalScore = 0;
        public ISEResultReader(string resultxml)
        {
            XmlDocument xmldoc = new XmlDocument();
            xmldoc.LoadXml(resultxml);
            XmlElement xeresult = xmldoc.DocumentElement;
            xeSentence = xeresult["read_sentence"]["rec_paper"]["read_chapter"]["sentence"];
            var xmlattributes = xeSentence.Attributes;
            _totalScore = Convert.ToDouble(xmlattributes["total_score"].Value.Replace("\"",""));
            _content = xmlattributes["content"].Value.Replace("\"", "");
            AnswerList = new List<ISEWord>();
            foreach (XmlElement word in xeSentence)
            {
                var wordattributes = word.Attributes;
                string content = wordattributes["content"].Value;
                var atotalscore = wordattributes["total_score"];
                if (content == "sil" || atotalscore == null)   
                    continue;
                double score = Convert.ToDouble(atotalscore.Value.Replace("\"", ""));
                ISEWord wordnow = new ISEWord
                {
                    Content = content,
                    Score = score,
                    IsPass = (score >= PassScore)
                };
                AnswerList.Add(wordnow);
            }
        }
        public struct ISEWord
        {
            public string Content;
            public double Score;
            public bool IsPass;
        }
    }
    public class ISEServerAgent
        :IDisposable
    {
        public string SessionID = null;
        public int errorCode = (int)ErrorCode.MSP_SUCCESS;
        public int epStatus
        {
            get
            {
                return _epstatus;
            }
        }
        public int recStatus
        {
            get
            {
                return _recstatus;
            }
        }
        private int _epstatus = (int)EpStatus.MSP_EP_LOOKING_FOR_SPEECH;
        private int _recstatus = (int)RecStatus.MSP_REC_STATUS_SUCCESS;
        public string Text
        {
            get
            {
                return _text;
            }
            set
            {
                _textlen = (uint)value.Length;
                _text = value;
            }
        }
        private string _text = null;
        private uint _textlen = 0;
        public uint textLen
        {
            get
            {
                return _textlen;
            }
        }
        public void Login(string appid)
        {
            string lgi_param = "appid = {0}";
            lgi_param = string.Format(lgi_param, appid);
            errorCode = ISEDLL.MSPLogin(null, null, lgi_param);
            if (errorCode == (int)ErrorCode.MSP_SUCCESS) 
            {
                string ise_ssb_param = "sub=ise,category=read_sentence,language=en_us,aue=speex-wb;7,auf=audio/L16;rate=16000";
                var hSessionID = ISEDLL.QISESessionBegin(ise_ssb_param, null, ref errorCode);
                if (errorCode == (int)ErrorCode.MSP_SUCCESS)
                {
                    SessionID = UnmanagedManager.GetStringFromUnmanagedMemory(hSessionID);
                }
            }
        }
        public void TextPut(string text)
        {
            Text = text;
            errorCode = ISEDLL.QISETextPut(SessionID, Text, textLen, null);
        }
        public void AudioWrite(Stream stream)
        {
            int size = (int)stream.Length;
            IntPtr h = UnmanagedManager.UnmanagedReader(stream, size);
            int pcmCount = 0;
            IntPtr hnow = h;
            int audStat = (int)AudioSample.MSP_AUDIO_SAMPLE_CONTINUE;
            while (true)
            {
                uint len = 6400;// 每次写入200ms音频(16k，16bit)：1帧音频20ms，10帧=200ms。16k采样率的16位音频，一帧的大小为640Byte
                if (size <= 2 * textLen)
                {
                    len = (uint)size;
                }
                if (len <= 0)
                {
                    break;
                }
                if (0 == pcmCount)
                {
                    audStat = (int)AudioSample.MSP_AUDIO_SAMPLE_FIRST;
                }
                errorCode = ISEDLL.QISEAudioWrite(SessionID, h + pcmCount, len, audStat, ref _epstatus, ref _recstatus);
                pcmCount += (int)len;
                size -= (int)len;
            }
            errorCode = ISEDLL.QISEAudioWrite(SessionID, h + pcmCount, 0, (int)AudioSample.MSP_AUDIO_SAMPLE_LAST, ref _epstatus, ref _recstatus);
        }
        public string GetAnswer()
        {
            uint resultLen = 0;
            while (recStatus != (int)RecStatus.MSP_REC_STATUS_COMPLETE)
            {
                Thread.Sleep(500);//要等到服务端生成结果，避免重复调用，因此建议异步使用
                IntPtr result = ISEDLL.QISEGetResult(SessionID, ref resultLen, ref _recstatus, ref errorCode);
                if (recStatus == (int)RecStatus.MSP_REC_STATUS_COMPLETE)
                    return UnmanagedManager.GetStringFromUnmanagedMemory(result);
            }
            return null;
        }
        public void Dispose()
        {
            errorCode = ISEDLL.QISESessionEnd(SessionID, null);
            errorCode = ISEDLL.MSPLogout();
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
        public static IntPtr UnmanagedReader(Stream s, int size)
        {
            IntPtr h = Marshal.AllocHGlobal(size);
            int ofs = 0;
            while (true)
            {
                int now = s.ReadByte();
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
        /// <param name="h">指向非托管代码字符串的指针</param>
        /// <returns>返回指针指向的字符串</returns>
        public static string GetStringFromUnmanagedMemory(IntPtr h)
        {
            List<byte> lb = new List<byte>();
            while (Marshal.ReadByte(h) != 0)
            {
                lb.Add(Marshal.ReadByte(h));
                h = h + 1;
            }
            byte[] bs = lb.ToArray();
            return Encoding.Default.GetString(lb.ToArray());
        }
    }
    public static class ISEDLL
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
        [DllImport(@"msc.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern int QISESessionEnd(string sessionID, string hints);
        [DllImport(@"msc.dll", CallingConvention = CallingConvention.Winapi)]
        public static extern int MSPLogout();
        #endregion
    }
    public enum ErrorCode
    {
        MSP_SUCCESS = 0,
        MSP_ERROR_FAIL = -1,
        MSP_ERROR_EXCEPTION = -2,

        /* General errors 10100(0x2774) */
        MSP_ERROR_GENERAL = 10100,     /* 0x2774 */
        MSP_ERROR_OUT_OF_MEMORY = 10101,     /* 0x2775 */
        MSP_ERROR_FILE_NOT_FOUND = 10102,     /* 0x2776 */
        MSP_ERROR_NOT_SUPPORT = 10103,     /* 0x2777 */
        MSP_ERROR_NOT_IMPLEMENT = 10104,     /* 0x2778 */
        MSP_ERROR_ACCESS = 10105,     /* 0x2779 */
        MSP_ERROR_INVALID_PARA = 10106,     /* 0x277A */
        MSP_ERROR_INVALID_PARA_VALUE = 10107,     /* 0x277B */
        MSP_ERROR_INVALID_HANDLE = 10108,     /* 0x277C */
        MSP_ERROR_INVALID_DATA = 10109,     /* 0x277D */
        MSP_ERROR_NO_LICENSE = 10110,     /* 0x277E */
        MSP_ERROR_NOT_INIT = 10111,     /* 0x277F */
        MSP_ERROR_NULL_HANDLE = 10112,     /* 0x2780 */
        MSP_ERROR_OVERFLOW = 10113,     /* 0x2781 */
        MSP_ERROR_TIME_OUT = 10114,     /* 0x2782 */
        MSP_ERROR_OPEN_FILE = 10115,     /* 0x2783 */
        MSP_ERROR_NOT_FOUND = 10116,     /* 0x2784 */
        MSP_ERROR_NO_ENOUGH_BUFFER = 10117,     /* 0x2785 */
        MSP_ERROR_NO_DATA = 10118,     /* 0x2786 */
        MSP_ERROR_NO_MORE_DATA = 10119,     /* 0x2787 */
        MSP_ERROR_SKIPPED = 10120,     /* 0x2788 */
        MSP_ERROR_ALREADY_EXIST = 10121,     /* 0x2789 */
        MSP_ERROR_LOAD_MODULE = 10122,     /* 0x278A */
        MSP_ERROR_BUSY = 10123,     /* 0x278B */
        MSP_ERROR_INVALID_CONFIG = 10124,     /* 0x278C */
        MSP_ERROR_VERSION_CHECK = 10125,     /* 0x278D */
        MSP_ERROR_CANCELED = 10126,     /* 0x278E */
        MSP_ERROR_INVALID_MEDIA_TYPE = 10127,     /* 0x278F */
        MSP_ERROR_CONFIG_INITIALIZE = 10128,     /* 0x2790 */
        MSP_ERROR_CREATE_HANDLE = 10129,     /* 0x2791 */
        MSP_ERROR_CODING_LIB_NOT_LOAD = 10130,     /* 0x2792 */

        /* Error codes of network 10200(0x27D8)*/
        MSP_ERROR_NET_GENERAL = 10200,     /* 0x27D8 */
        MSP_ERROR_NET_OPENSOCK = 10201,     /* 0x27D9 */   /* Open socket */
        MSP_ERROR_NET_CONNECTSOCK = 10202,     /* 0x27DA */   /* Connect socket */
        MSP_ERROR_NET_ACCEPTSOCK = 10203,     /* 0x27DB */   /* Accept socket */
        MSP_ERROR_NET_SENDSOCK = 10204,     /* 0x27DC */   /* Send socket data */
        MSP_ERROR_NET_RECVSOCK = 10205,     /* 0x27DD */   /* Recv socket data */
        MSP_ERROR_NET_INVALIDSOCK = 10206,     /* 0x27DE */   /* Invalid socket handle */
        MSP_ERROR_NET_BADADDRESS = 10207,     /* 0x27EF */   /* Bad network address */
        MSP_ERROR_NET_BINDSEQUENCE = 10208,     /* 0x27E0 */   /* Bind after listen/connect */
        MSP_ERROR_NET_NOTOPENSOCK = 10209,     /* 0x27E1 */   /* Socket is not opened */
        MSP_ERROR_NET_NOTBIND = 10210,     /* 0x27E2 */   /* Socket is not bind to an address */
        MSP_ERROR_NET_NOTLISTEN = 10211,     /* 0x27E3 */   /* Socket is not listenning */
        MSP_ERROR_NET_CONNECTCLOSE = 10212,     /* 0x27E4 */   /* The other side of connection is closed */
        MSP_ERROR_NET_NOTDGRAMSOCK = 10213,     /* 0x27E5 */   /* The socket is not datagram type */

        /* Error codes of mssp message 10300(0x283C) */
        MSP_ERROR_MSG_GENERAL = 10300,     /* 0x283C */
        MSP_ERROR_MSG_PARSE_ERROR = 10301,     /* 0x283D */
        MSP_ERROR_MSG_BUILD_ERROR = 10302,     /* 0x283E */
        MSP_ERROR_MSG_PARAM_ERROR = 10303,     /* 0x283F */
        MSP_ERROR_MSG_CONTENT_EMPTY = 10304,     /* 0x2840 */
        MSP_ERROR_MSG_INVALID_CONTENT_TYPE = 10305,     /* 0x2841 */
        MSP_ERROR_MSG_INVALID_CONTENT_LENGTH = 10306,     /* 0x2842 */
        MSP_ERROR_MSG_INVALID_CONTENT_ENCODE = 10307,     /* 0x2843 */
        MSP_ERROR_MSG_INVALID_KEY = 10308,     /* 0x2844 */
        MSP_ERROR_MSG_KEY_EMPTY = 10309,     /* 0x2845 */
        MSP_ERROR_MSG_SESSION_ID_EMPTY = 10310,     /* 0x2846 */
        MSP_ERROR_MSG_LOGIN_ID_EMPTY = 10311,     /* 0x2847 */
        MSP_ERROR_MSG_SYNC_ID_EMPTY = 10312,     /* 0x2848 */
        MSP_ERROR_MSG_APP_ID_EMPTY = 10313,     /* 0x2849 */
        MSP_ERROR_MSG_EXTERN_ID_EMPTY = 10314,     /* 0x284A */
        MSP_ERROR_MSG_INVALID_CMD = 10315,     /* 0x284B */
        MSP_ERROR_MSG_INVALID_SUBJECT = 10316,     /* 0x284C */
        MSP_ERROR_MSG_INVALID_VERSION = 10317,     /* 0x284D */
        MSP_ERROR_MSG_NO_CMD = 10318,     /* 0x284E */
        MSP_ERROR_MSG_NO_SUBJECT = 10319,     /* 0x284F */
        MSP_ERROR_MSG_NO_VERSION = 10320,     /* 0x2850 */
        MSP_ERROR_MSG_MSSP_EMPTY = 10321,     /* 0x2851 */
        MSP_ERROR_MSG_NEW_RESPONSE = 10322,     /* 0x2852 */
        MSP_ERROR_MSG_NEW_CONTENT = 10323,     /* 0x2853 */
        MSP_ERROR_MSG_INVALID_SESSION_ID = 10324,     /* 0x2854 */

        /* Error codes of DataBase 10400(0x28A0)*/
        MSP_ERROR_DB_GENERAL = 10400,     /* 0x28A0 */
        MSP_ERROR_DB_EXCEPTION = 10401,     /* 0x28A1 */
        MSP_ERROR_DB_NO_RESULT = 10402,     /* 0x28A2 */
        MSP_ERROR_DB_INVALID_USER = 10403,     /* 0x28A3 */
        MSP_ERROR_DB_INVALID_PWD = 10404,     /* 0x28A4 */
        MSP_ERROR_DB_CONNECT = 10405,     /* 0x28A5 */
        MSP_ERROR_DB_INVALID_SQL = 10406,     /* 0x28A6 */
        MSP_ERROR_DB_INVALID_APPID = 10407,    /* 0x28A7 */

        /* Error codes of Resource 10500(0x2904)*/
        MSP_ERROR_RES_GENERAL = 10500,     /* 0x2904 */
        MSP_ERROR_RES_LOAD = 10501,     /* 0x2905 */   /* Load resource */
        MSP_ERROR_RES_FREE = 10502,     /* 0x2906 */   /* Free resource */
        MSP_ERROR_RES_MISSING = 10503,     /* 0x2907 */   /* Resource File Missing */
        MSP_ERROR_RES_INVALID_NAME = 10504,     /* 0x2908 */   /* Invalid resource file name */
        MSP_ERROR_RES_INVALID_ID = 10505,     /* 0x2909 */   /* Invalid resource ID */
        MSP_ERROR_RES_INVALID_IMG = 10506,     /* 0x290A */   /* Invalid resource image pointer */
        MSP_ERROR_RES_WRITE = 10507,     /* 0x290B */   /* Write read-only resource */
        MSP_ERROR_RES_LEAK = 10508,     /* 0x290C */   /* Resource leak out */
        MSP_ERROR_RES_HEAD = 10509,     /* 0x290D */   /* Resource head currupt */
        MSP_ERROR_RES_DATA = 10510,     /* 0x290E */   /* Resource data currupt */
        MSP_ERROR_RES_SKIP = 10511,     /* 0x290F */   /* Resource file skipped */

        /* Error codes of TTS 10600(0x2968)*/
        MSP_ERROR_TTS_GENERAL = 10600,     /* 0x2968 */
        MSP_ERROR_TTS_TEXTEND = 10601,     /* 0x2969 */  /* Meet text end */
        MSP_ERROR_TTS_TEXT_EMPTY = 10602,     /* 0x296A */  /* no synth text */

        /* Error codes of Recognizer 10700(0x29CC) */
        MSP_ERROR_REC_GENERAL = 10700,     /* 0x29CC */
        MSP_ERROR_REC_INACTIVE = 10701,     /* 0x29CD */
        MSP_ERROR_REC_GRAMMAR_ERROR = 10702,     /* 0x29CE */
        MSP_ERROR_REC_NO_ACTIVE_GRAMMARS = 10703,     /* 0x29CF */
        MSP_ERROR_REC_DUPLICATE_GRAMMAR = 10704,     /* 0x29D0 */
        MSP_ERROR_REC_INVALID_MEDIA_TYPE = 10705,     /* 0x29D1 */
        MSP_ERROR_REC_INVALID_LANGUAGE = 10706,     /* 0x29D2 */
        MSP_ERROR_REC_URI_NOT_FOUND = 10707,     /* 0x29D3 */
        MSP_ERROR_REC_URI_TIMEOUT = 10708,     /* 0x29D4 */
        MSP_ERROR_REC_URI_FETCH_ERROR = 10709,     /* 0x29D5 */

        /* Error codes of Speech Detector 10800(0x2A30) */
        MSP_ERROR_EP_GENERAL = 10800,     /* 0x2A30 */
        MSP_ERROR_EP_NO_SESSION_NAME = 10801,     /* 0x2A31 */
        MSP_ERROR_EP_INACTIVE = 10802,     /* 0x2A32 */
        MSP_ERROR_EP_INITIALIZED = 10803,     /* 0x2A33 */

        /* Error codes of TUV */
        MSP_ERROR_TUV_GENERAL = 10900,     /* 0x2A94 */
        MSP_ERROR_TUV_GETHIDPARAM = 10901,     /* 0x2A95 */   /* Get Busin Param huanid*/
        MSP_ERROR_TUV_TOKEN = 10902,     /* 0x2A96 */   /* Get Token */
        MSP_ERROR_TUV_CFGFILE = 10903,     /* 0x2A97 */   /* Open cfg file */
        MSP_ERROR_TUV_RECV_CONTENT = 10904,     /* 0x2A98 */   /* received content is error */
        MSP_ERROR_TUV_VERFAIL = 10905,     /* 0x2A99 */   /* Verify failure */

        /* Error codes of IMTV */
        MSP_ERROR_IMTV_SUCCESS = 11000,     /* 0x2AF8 */   /* 成功 */
        MSP_ERROR_IMTV_NO_LICENSE = 11001,     /* 0x2AF9 */   /* 试用次数结束，用户需要付费 */
        MSP_ERROR_IMTV_SESSIONID_INVALID = 11002,     /* 0x2AFA */   /* SessionId失效，需要重新登录通行证 */
        MSP_ERROR_IMTV_SESSIONID_ERROR = 11003,     /* 0x2AFB */   /* SessionId为空，或者非法 */
        MSP_ERROR_IMTV_UNLOGIN = 11004,     /* 0x2AFC */   /* 未登录通行证 */
        MSP_ERROR_IMTV_SYSTEM_ERROR = 11005,     /* 0x2AFD */   /* 系统错误 */

        /* Error codes of HCR */
        MSP_ERROR_HCR_GENERAL = 11100,
        MSP_ERROR_HCR_RESOURCE_NOT_EXIST = 11101,

        /* Error codes of http 12000(0x2EE0) */
        MSP_ERROR_HTTP_BASE = 12000,    /* 0x2EE0 */

        /*Error codes of ISV */
        MSP_ERROR_ISV_NO_USER = 13000,    /* 32C8 */    /* the user doesn't exist */
    }
    /**
     *  MSPSampleStatus indicates how the sample buffer should be handled
     *  MSP_AUDIO_SAMPLE_FIRST		- The sample buffer is the start of audio
     *								  If recognizer was already recognizing, it will discard
     *								  audio received to date and re-start the recognition
     *  MSP_AUDIO_SAMPLE_CONTINUE	- The sample buffer is continuing audio
     *  MSP_AUDIO_SAMPLE_LAST		- The sample buffer is the end of audio
     *								  The recognizer will cease processing audio and
     *								  return results
     *  Note that sample statii can be combined; for example, for file-based input
     *  the entire file can be written with SAMPLE_FIRST | SAMPLE_LAST as the
     *  status.
     *  Other flags may be added in future to indicate other special audio
     *  conditions such as the presence of AGC
     */
    public enum AudioSample
    {
        MSP_AUDIO_SAMPLE_INIT = 0x00,
        MSP_AUDIO_SAMPLE_FIRST = 0x01,
        MSP_AUDIO_SAMPLE_CONTINUE = 0x02,
        MSP_AUDIO_SAMPLE_LAST = 0x04,
    };
    /*
     *  The enumeration MSPRecognizerStatus contains the recognition status
     *  MSP_REC_STATUS_SUCCESS				- successful recognition with partial results
     *  MSP_REC_STATUS_NO_MATCH				- recognition rejected
     *  MSP_REC_STATUS_INCOMPLETE			- recognizer needs more time to compute results
     *  MSP_REC_STATUS_NON_SPEECH_DETECTED	- discard status, no more in use
     *  MSP_REC_STATUS_SPEECH_DETECTED		- recognizer has detected audio, this is delayed status
     *  MSP_REC_STATUS_COMPLETE				- recognizer has return all result
     *  MSP_REC_STATUS_MAX_CPU_TIME			- CPU time limit exceeded
     *  MSP_REC_STATUS_MAX_SPEECH			- maximum speech length exceeded, partial results may be returned
     *  MSP_REC_STATUS_STOPPED				- recognition was stopped
     *  MSP_REC_STATUS_REJECTED				- recognizer rejected due to low confidence
     *  MSP_REC_STATUS_NO_SPEECH_FOUND		- recognizer still found no audio, this is delayed status
     */
    public enum RecStatus
    {
        MSP_REC_STATUS_SUCCESS = 0,
        MSP_REC_STATUS_NO_MATCH = 1,
        MSP_REC_STATUS_INCOMPLETE = 2,
        MSP_REC_STATUS_NON_SPEECH_DETECTED = 3,
        MSP_REC_STATUS_SPEECH_DETECTED = 4,
        MSP_REC_STATUS_COMPLETE = 5,
        MSP_REC_STATUS_MAX_CPU_TIME = 6,
        MSP_REC_STATUS_MAX_SPEECH = 7,
        MSP_REC_STATUS_STOPPED = 8,
        MSP_REC_STATUS_REJECTED = 9,
        MSP_REC_STATUS_NO_SPEECH_FOUND = 10,
        MSP_REC_STATUS_FAILURE = MSP_REC_STATUS_NO_MATCH,
    };
    /**
     * The enumeration MSPepState contains the current endpointer state
     *  MSP_EP_LOOKING_FOR_SPEECH	- Have not yet found the beginning of speech
     *  MSP_EP_IN_SPEECH			- Have found the beginning, but not the end of speech
     *  MSP_EP_AFTER_SPEECH			- Have found the beginning and end of speech
     *  MSP_EP_TIMEOUT				- Have not found any audio till timeout
     *  MSP_EP_ERROR				- The endpointer has encountered a serious error
     *  MSP_EP_MAX_SPEECH			- Have arrive the max size of speech
     */
    public enum EpStatus
    {
        MSP_EP_LOOKING_FOR_SPEECH = 0,
        MSP_EP_IN_SPEECH = 1,
        MSP_EP_AFTER_SPEECH = 3,
        MSP_EP_TIMEOUT = 4,
        MSP_EP_ERROR = 5,
        MSP_EP_MAX_SPEECH = 6,
        MSP_EP_IDLE = 7  // internal state after stop and before start
    };
    [Flags]
    /* Upload data process flags */
    public enum DataSample
    {
        MSP_DATA_SAMPLE_INIT = 0x00,
        MSP_DATA_SAMPLE_FIRST = 0x01,
        MSP_DATA_SAMPLE_CONTINUE = 0x02,
        MSP_DATA_SAMPLE_LAST = 0x04,
    };

}
