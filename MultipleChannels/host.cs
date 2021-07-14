using NAudio.Utils;
using NAudio.Wave.Asio;
using System;
using System.IO.Ports;
using System.Linq;
//using CircularBuffer;


/*****
 * Datatypes & C++ equivalants
 * 
 *     +---+---------+----------+----------+-----------+
 *     | # | C#      | C++      | Length   | Signed ?  |
 *     +===+=========+==========+==========+===========+
 *     | 1 | byte    | uint8_t  | 8 bits   | Unsigned  |
 *     +---+---------+----------+----------+-----------+
 *     | 2 | bool    | bool     | 8 bits   | Unsigned  |
 *     +---+---------+----------+----------+-----------+
 *     | 3 | UInt16  | uint16_t | 16 bits  | Unsigned  |
 *     +---+---------+----------+----------+-----------+
 *     | 4 | UInt32  | uint32_t | 32 bits  | Unsigned  |
 *     +---+---------+----------+----------+-----------+
 */




namespace Host
{
    internal class ConnectionManager 
    {
        /// <summary> Mode selection for streaming at 4000 Hz sampling frequency, max representable frequency is 2000 Hz. </summary>
        public const byte MODE_STREAM4K = 10;
        /// <summary> Mode selection for streaming at 3000 Hz sampling frequency, max representable frequency is 1500 Hz. </summary>
        public const byte MODE_STREAM3K = 11;
        /// <summary> Mode selection for streaming at 2000 Hz sampling frequency, max representable frequency is 1000 Hz. </summary>
        public const byte MODE_STREAM2K = 12;
        /// <summary> Mode selection for streaming at 1000 Hz sampling frequency, max representable frequency is 500 Hz. </summary>
        public const byte MODE_STREAM1K = 13;
        /// <summary> Mode selection for streaming at 500 Hz sampling frequency, max representable frequency is 250 Hz. </summary>
        public const byte MODE_STREAMHK = 14;
        /// <summary> Mode selection for putting the device to sleep, no output will be shown. </summary>
        public const byte MODE_SLEEP    = 15;

        private const byte MSG_ERROR = 1;
        private const byte MSG_INFO  = 2;
        private const byte MSG_DEBUG = 3;
        private const byte MSG_LEVEL = 3;

        private const string DISCOVER_STR_DAT = "DHData";
        private const string DISCOVER_STR_CMD  = "DHCommand";

        private const byte MAX_CHANNELS = 100;

        private bool _state_connect  = false;
        private bool _state_discover = false;
        private bool _state_stream   = false;
        private bool _state_sleep    = false;

        private string _dat_port_str = null;
        private string _cmd_port_str = null;
        private SerialPort _dat_port = null;
        private SerialPort _cmd_port = null;

        private bool[] _channels_enabled; 
        private UInt16 _samples_per_channel;
        //private CircularBuffer<UInt16>[] _two_byte_buffer; 

        private readonly int outputChannels = 40;
        private readonly int inputChannels;
        private readonly float[,] routingMatrix;
        private float[] mixBuffer;


        public ConnectionManager()
        {
            DiscoverPort();
            Connect();
        }

        ~ConnectionManager()
        {
            Disconnect();
        }

        private void DiscoverPort()
        {
            string[] ports = SerialPort.GetPortNames();
            ports = ports.Distinct().ToArray();
            Log(MSG_DEBUG,string.Join(", ", ports));

            SerialPort discoveryPort = null;
            foreach (string port in ports)
            {
                try
                {
                    discoveryPort = new SerialPort(port);
                    discoveryPort.Open();
                    discoveryPort.ReadTimeout = 1500;

                    string message = discoveryPort.ReadLine();
                    Log(MSG_INFO, port + " - " + message);
                    if (message == DISCOVER_STR_DAT) _dat_port_str = port;
                    if (message == DISCOVER_STR_CMD) _cmd_port_str = port;
                    discoveryPort.Close();
                }
                catch (Exception)
                {
                    if(discoveryPort != null)
                        discoveryPort.Close();
                    Log(MSG_DEBUG, "Timed Out on Port: " + port);
                }

                if (_dat_port_str != null && _cmd_port_str != null)
                {
                    _state_discover = true;
                }
            }

        }

        private void Log(byte type = 0, string msg = "")
        {
            if (type > MSG_LEVEL)  //if the console log message is higher than the required level, do not do anything. 
                return; 

            switch(type)
            {
                case MSG_ERROR: Console.Write("(E) ");  break;
                case MSG_INFO:  Console.Write("(I) ");  break;
                case MSG_DEBUG: Console.Write("(D) ");  break;
                default:        Console.Write("(O) ");  break;
            }
            Console.WriteLine(msg);
        }

        private void Connect()
        {
            if(_state_connect == false && _state_discover == true)
            {
                try 
                { 
                    _dat_port = new SerialPort(_dat_port_str);
                    _cmd_port  = new SerialPort(_cmd_port_str);

                    _dat_port.Open();
                    _cmd_port.Open();
                    Log(MSG_INFO, "Ports connected and open.");
                    _state_connect = true;
                }
                catch(Exception)
                {
                    Log(MSG_ERROR, "Failed opening ports.");
                }

            }
            else
            {
                Log(MSG_ERROR, "At least one of the ports is already connected, try disconnecting, or discovery didn't find any device.");
            }
        }

        private void Disconnect()
        {
            if (_state_connect == true)
            {
                try
                {
                    _cmd_port.Close();
                    _dat_port.Close();
                    _cmd_port = null;
                    _dat_port = null;
                    Log(MSG_INFO, "Ports disconnected and closed.");
                    _state_connect = false;
                }
                catch (Exception)
                {
                    Log(MSG_ERROR, "Failed closing ports.");
                }
            }
            else
            {
                Log(MSG_ERROR, "Ports are not initalized.");
            }
        }


        /// <summary>
        /// Swiches the device between various STREAM modes and SLEEP. 
        /// </summary>
        /// <param name="mode">use the MODE_** constants to pass the parameters.</param>
        public bool SetMode(byte mode)
        {
            bool ret_val = false;
  
            switch(mode)
            {
                case MODE_SLEEP:    _cmd_port.Write("SLEEP");    _state_stream = false; ret_val = true; break;
                case MODE_STREAM4K: _cmd_port.Write("STREAM4K"); _state_stream = true;  ret_val = true; break;
                case MODE_STREAM3K: _cmd_port.Write("STREAM3K"); _state_stream = true;  ret_val = true; break;
                case MODE_STREAM2K: _cmd_port.Write("STREAM2K"); _state_stream = true;  ret_val = true; break;
                case MODE_STREAM1K: _cmd_port.Write("STREAM1K"); _state_stream = true;  ret_val = true; break;
                case MODE_STREAMHK: _cmd_port.Write("STREAMHK"); _state_stream = true;  ret_val = true; break; 
            }
            return ret_val;
        }
    

        public void SetupBuffers(IntPtr[] inBuffers, IntPtr[] outBuffers, int sampleCount, AsioSampleType sampleType)
        {
            Func<IntPtr, int, float> getInputSample;
            if (sampleType == AsioSampleType.Int32LSB)
                getInputSample = GetInputSampleInt32LSB;
            else if (sampleType == AsioSampleType.Int16LSB)
                getInputSample = GetInputSampleInt16LSB;
            else if (sampleType == AsioSampleType.Int24LSB)
                getInputSample = GetInputSampleInt24LSB;
            else if (sampleType == AsioSampleType.Float32LSB)
                getInputSample = GetInputSampleFloat32LSB;
            else
                throw new ArgumentException($"Unsupported ASIO sample type {sampleType}");

            int offset = 0;
            mixBuffer = BufferHelpers.Ensure(mixBuffer, sampleCount * outputChannels);

            for (int n = 0; n < sampleCount; n++)
            {
                for (int outputChannel = 0; outputChannel < outputChannels; outputChannel++)
                {
                    mixBuffer[offset] = 0.0f;
                    for (int inputChannel = 0; inputChannel < inputChannels; inputChannel++)
                    {
                        // mix in the desired amount
                        var amount = routingMatrix[inputChannel, outputChannel];
                        if (amount > 0)
                            mixBuffer[offset] += amount * getInputSample(inBuffers[inputChannel], n);
                    }
                    offset++;
                }
            }

            Action<IntPtr, int, float> setOutputSample;
            if (sampleType == AsioSampleType.Int32LSB)
                setOutputSample = SetOutputSampleInt32LSB;
            else if (sampleType == AsioSampleType.Int16LSB)
                setOutputSample = SetOutputSampleInt16LSB;
            else if (sampleType == AsioSampleType.Int24LSB)
                throw new InvalidOperationException("Not supported");
            else if (sampleType == AsioSampleType.Float32LSB)
                setOutputSample = SetOutputSampleFloat32LSB;
            else
                throw new ArgumentException($"Unsupported ASIO sample type {sampleType}");


            // now write to the output buffers
            offset = 0;
            for (int n = 0; n < sampleCount; n++)
            {
                for (int outputChannel = 0; outputChannel < outputChannels; outputChannel++)
                {
                    setOutputSample(outBuffers[outputChannel], n, mixBuffer[offset++]);
                }
            }
        }



        public bool WriteSample(UInt16 data, byte channel)
        {

            //if (_channels_enabled[channel] == true && !_two_byte_buffer[channel].IsFull)    //check if channel is enabled AND buffer is not full
            //{
            //    //_two_byte_buffer[channel].PushBack(data);
            //    return true;
            //}
            //else
            //{
            //    Log(MSG_ERROR, "Buffer Full/Overflow or Attempt to use uninitalized channel: " + channel);
                return false;
            //}

                
           
        }

        

        private void PublishByteBuffer()
        {

            //for (int i = 0; i < MAX_CHANNELS; i++)
            //{
            //    for( int j = 0; j < _two_byte_buffer[i].Size; j ++)
            //    {
            //        UInt16 sample = _two_byte_buffer[i].Front();
            //        _two_byte_buffer[i].PopFront();
               
            //        _one_byte_buffer[i * j] = (byte)((sample & 0x00FF0000) >> 16);
            //        _one_byte_buffer[i * j + 1] = (byte)((sample & 0x0000FFFF) >> 00);
            //    }
            //}




        }
        private unsafe void SetOutputSampleInt32LSB(IntPtr buffer, int n, float value)
        {
            *((int*)buffer + n) = (int)(value * int.MaxValue);
        }

        private unsafe float GetInputSampleInt32LSB(IntPtr inputBuffer, int n)
        {
            return *((int*)inputBuffer + n) / (float)int.MaxValue;
        }

        private unsafe float GetInputSampleInt16LSB(IntPtr inputBuffer, int n)
        {
            return *((short*)inputBuffer + n) / (float)short.MaxValue;
        }

        private unsafe void SetOutputSampleInt16LSB(IntPtr buffer, int n, float value)
        {
            *((short*)buffer + n) = (short)(value * short.MaxValue);
        }

        private unsafe float GetInputSampleInt24LSB(IntPtr inputBuffer, int n)
        {
            byte* pSample = (byte*)inputBuffer + n * 3;
            int sample = pSample[0] | (pSample[1] << 8) | ((sbyte)pSample[2] << 16);
            return sample / 8388608.0f;
        }


        private unsafe float GetInputSampleFloat32LSB(IntPtr inputBuffer, int n)
        {
            return *((float*)inputBuffer + n);
        }

        private unsafe void SetOutputSampleFloat32LSB(IntPtr buffer, int n, float value)
        {
            *((float*)buffer + n) = value;
        }


    }

}