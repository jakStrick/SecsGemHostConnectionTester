using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;

namespace SecsGemClient
{
    // SECS-II Message Structure
    public class SecsMessage
    {
        public byte Stream { get; set; }
        public byte Function { get; set; }
        public bool WBit { get; set; }
        public ushort SessionId { get; set; }
        public uint SystemBytes { get; set; }
        public byte[]? Data { get; set; }
        public SecsItem? RootItem { get; set; }

        public override string ToString()
        {
            var direction = WBit ? "W" : "";
            return $"S{Stream}F{Function}{direction} SysBytes: 0x{SystemBytes:X8}";
        }

        public string GetDetailedString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"S{Stream}F{Function}{(WBit ? "W" : "")} - System: 0x{SystemBytes:X8}, Session: {SessionId}");

            if (RootItem != null)
            {
                sb.AppendLine("Data Structure:");
                sb.AppendLine(RootItem.ToString());
            }

            return sb.ToString();
        }
    }

    // SECS-II Message Processor
    public class SecsMessageProcessor
    {
        public event EventHandler<MessageProcessedEventArgs>? MessageProcessed;

        public static SecsMessage CreateS1F1Message()
        {
            // S1F1 - Are You There (W)
            return new SecsMessage
            {
                Stream = 1,
                Function = 1,
                WBit = true,
                RootItem = null // No data for S1F1
            };
        }

        public SecsMessage CreateS1F2Message()
        {
            // S1F2 - On Line Data (Response to S1F1)
            var onlineData = new SecsListItem(new List<SecsItem>
            {
                new SecsAsciiItem("VIRTUAL_HOST"),     // Model
                new SecsAsciiItem("1.0.0")             // Software Version
            });

            return new SecsMessage
            {
                Stream = 1,
                Function = 2,
                WBit = false,
                RootItem = onlineData
            };
        }

        public SecsMessage CreateS1F13Message()
        {
            // S1F13 - Establish Communications Request (W)
            return new SecsMessage
            {
                Stream = 1,
                Function = 13,
                WBit = true,
                RootItem = null
            };
        }

        public SecsMessage CreateS1F14Message()
        {
            // S1F14 - Establish Communications Acknowledge
            var commAck = new SecsListItem(new List<SecsItem>
            {
                new SecsBinaryItem(new byte[] { 0x00 }),  // COMMACK (0 = OK)
                new SecsListItem(new List<SecsItem>       // Data ID List
                {
                    new SecsAsciiItem("VirtualHost"),
                    new SecsAsciiItem("1.0.0")
                })
            });

            return new SecsMessage
            {
                Stream = 1,
                Function = 14,
                WBit = false,
                RootItem = commAck
            };
        }

        public SecsMessage CreateS2F17Message()
        {
            // S2F17 - Date and Time Request (W)
            return new SecsMessage
            {
                Stream = 2,
                Function = 17,
                WBit = true,
                RootItem = null
            };
        }

        public SecsMessage CreateS2F18Message()
        {
            // S2F18 - Date and Time Data
            var now = DateTime.Now;
            var timeData = new SecsAsciiItem(now.ToString("yyyyMMddHHmmss"));

            return new SecsMessage
            {
                Stream = 2,
                Function = 18,
                WBit = false,
                RootItem = timeData
            };
        }

        public SecsMessage CreateS5F1Message(uint alarmId, string alarmText)
        {
            // S5F1 - Alarm Report Send
            var alarmData = new SecsListItem(new List<SecsItem>
            {
                new SecsBinaryItem(BitConverter.GetBytes(alarmId)),  // ALID
                new SecsBinaryItem(new byte[] { 0x80 }),             // ALCD (Set)
                new SecsAsciiItem(alarmText)                         // ALTX
            });

            return new SecsMessage
            {
                Stream = 5,
                Function = 1,
                WBit = true,
                RootItem = alarmData
            };
        }

        public SecsMessage CreateS6F11Message(uint eventId, Dictionary<string, object> reportData)
        {
            // S6F11 - Event Report Send
            var reportItems = new List<SecsItem>();

            foreach (var kvp in reportData)
            {
                switch (kvp.Value)
                {
                    case string str:
                        reportItems.Add(new SecsAsciiItem(str));
                        break;
                    case int intVal:
                        reportItems.Add(new SecsU4Item((uint)intVal));
                        break;
                    case float floatVal:
                        reportItems.Add(new SecsF4Item(floatVal));
                        break;
                    default:
                        reportItems.Add(new SecsAsciiItem(kvp.Value?.ToString() ?? ""));
                        break;
                }
            }

            var eventData = new SecsListItem(new List<SecsItem>
            {
                new SecsU4Item(eventId),                  // CEID
                new SecsListItem(reportItems)             // Report data
            });

            return new SecsMessage
            {
                Stream = 6,
                Function = 11,
                WBit = true,
                RootItem = eventData
            };
        }

        public SecsMessage CreateS7F1Message(string processProgram)
        {
            // S7F1 - Process Program Load Inquire (W)
            var ppBody = new SecsListItem(new List<SecsItem>
            {
                new SecsAsciiItem(processProgram),       // PPID
                new SecsBinaryItem(new byte[0])          // PPBODY (empty for inquire)
            });

            return new SecsMessage
            {
                Stream = 7,
                Function = 1,
                WBit = true,
                RootItem = ppBody
            };
        }

        public SecsMessage ParseMessage(byte[] messageData)
        {
            if (messageData == null || messageData.Length == 0)
                return new SecsMessage { RootItem = null };

            try
            {
                using (var stream = new MemoryStream(messageData))
                using (var reader = new BinaryReader(stream))
                {
                    var rootItem = ParseSecsItem(reader);
                    return new SecsMessage { RootItem = rootItem };
                }
            }
            catch (Exception ex)
            {
                OnMessageProcessed($"Error parsing message: {ex.Message}");
                return new SecsMessage { RootItem = null };
            }
        }

        private SecsItem? ParseSecsItem(BinaryReader reader)
        {
            if (reader.BaseStream.Position >= reader.BaseStream.Length)
                return null;

            var formatByte = reader.ReadByte();
            var formatCode = (SecsFormat)((formatByte & 0xFC) >> 2);
            var lengthBytes = formatByte & 0x03;

            uint length = 0;
            for (int i = 0; i < lengthBytes; i++)
            {
                length = (length << 8) | reader.ReadByte();
            }

            switch (formatCode)
            {
                case SecsFormat.List:
                    return ParseListItem(reader, (int)length);
                case SecsFormat.Binary:
                    return new SecsBinaryItem(reader.ReadBytes((int)length));
                case SecsFormat.Boolean:
                    return new SecsBooleanItem(reader.ReadBytes((int)length));
                case SecsFormat.ASCII:
                    return new SecsAsciiItem(Encoding.ASCII.GetString(reader.ReadBytes((int)length)));
                case SecsFormat.JIS8:
                    return new SecsJis8Item(reader.ReadBytes((int)length));
                case SecsFormat.I1:
                    return new SecsI1Item(reader.ReadBytes((int)length));
                case SecsFormat.I2:
                    return new SecsI2Item(reader.ReadBytes((int)length));
                case SecsFormat.I4:
                    return new SecsI4Item(reader.ReadBytes((int)length));
                case SecsFormat.I8:
                    return new SecsI8Item(reader.ReadBytes((int)length));
                case SecsFormat.F4:
                    return new SecsF4Item(BitConverter.ToSingle(reader.ReadBytes(4), 0));
                case SecsFormat.F8:
                    return new SecsF8Item(BitConverter.ToDouble(reader.ReadBytes(8), 0));
                case SecsFormat.U1:
                    return new SecsU1Item(reader.ReadBytes((int)length));
                case SecsFormat.U2:
                    return new SecsU2Item(reader.ReadBytes((int)length));
                case SecsFormat.U4:
                    return new SecsU4Item(BitConverter.ToUInt32(reader.ReadBytes(4), 0));
                case SecsFormat.U8:
                    return new SecsU8Item(reader.ReadBytes((int)length));
                default:
                    return new SecsBinaryItem(reader.ReadBytes((int)length));
            }
        }

        private SecsListItem ParseListItem(BinaryReader reader, int itemCount)
        {
            var items = new List<SecsItem>();

            for (int i = 0; i < itemCount; i++)
            {
                var item = ParseSecsItem(reader);
                if (item != null)
                    items.Add(item);
            }

            return new SecsListItem(items);
        }

        public byte[] SerializeMessage(SecsMessage message)
        {
            if (message.RootItem == null)
                return new byte[0];

            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                SerializeSecsItem(writer, message.RootItem);
                return stream.ToArray();
            }
        }

        private void SerializeSecsItem(BinaryWriter writer, SecsItem item)
        {
            var data = item.GetBytes();
            var formatCode = (byte)item.Format;
            var length = (uint)data.Length;

            // Determine length bytes needed
            byte lengthBytes = 1;
            if (length > 0xFFFF) lengthBytes = 3;
            else if (length > 0xFF) lengthBytes = 2;

            // Write format byte
            var formatByte = (byte)((formatCode << 2) | lengthBytes);
            writer.Write(formatByte);

            // Write length
            for (int i = lengthBytes - 1; i >= 0; i--)
            {
                writer.Write((byte)((length >> (8 * i)) & 0xFF));
            }

            // Write data
            writer.Write(data);
        }

        protected virtual void OnMessageProcessed(string result)
        {
            MessageProcessed?.Invoke(this, new MessageProcessedEventArgs(result));
        }
    }

    // SECS Data Formats (SEMI E5)
    public enum SecsFormat : byte
    {
        List = 0x00,      // 000000
        Binary = 0x08,    // 001000
        Boolean = 0x09,   // 001001
        ASCII = 0x10,     // 010000
        JIS8 = 0x11,      // 010001
        I1 = 0x18,        // 011000 (1-byte signed integer)
        I2 = 0x19,        // 011001 (2-byte signed integer)
        I4 = 0x1A,        // 011010 (4-byte signed integer)
        I8 = 0x1B,        // 011011 (8-byte signed integer)
        F4 = 0x20,        // 100000 (4-byte float)
        F8 = 0x21,        // 100001 (8-byte float)
        U1 = 0x28,        // 101000 (1-byte unsigned integer)
        U2 = 0x29,        // 101001 (2-byte unsigned integer)
        U4 = 0x2A,        // 101010 (4-byte unsigned integer)
        U8 = 0x2B         // 101011 (8-byte unsigned integer)
    }

    // Base SECS Item Class
    public abstract class SecsItem
    {
        public abstract SecsFormat Format { get; }
        public abstract byte[] GetBytes();
        public abstract override string ToString();
    }

    // SECS List Item
    public class SecsListItem : SecsItem
    {
        public override SecsFormat Format => SecsFormat.List;
        public List<SecsItem> Items { get; set; }

        public SecsListItem(List<SecsItem> items)
        {
            Items = items ?? new List<SecsItem>();
        }

        public override byte[] GetBytes()
        {
            // For lists, the length represents the number of items, not byte count
            return BitConverter.GetBytes((uint)Items.Count);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<L[{Items.Count}]");

            foreach (var item in Items)
            {
                var itemStr = item.ToString();
                var lines = itemStr.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    sb.AppendLine($"  {line}");
                }
            }

            sb.AppendLine(">");
            return sb.ToString();
        }
    }

    // SECS ASCII Item
    public class SecsAsciiItem : SecsItem
    {
        public override SecsFormat Format => SecsFormat.ASCII;
        public string Value { get; set; }

        public SecsAsciiItem(string value)
        {
            Value = value ?? "";
        }

        public override byte[] GetBytes()
        {
            return Encoding.ASCII.GetBytes(Value);
        }

        public override string ToString()
        {
            return $"<A[{Value.Length}] \"{Value}\">";
        }
    }

    // SECS Binary Item
    public class SecsBinaryItem : SecsItem
    {
        public override SecsFormat Format => SecsFormat.Binary;
        public byte[] Value { get; set; }

        public SecsBinaryItem(byte[] value)
        {
            Value = value ?? new byte[0];
        }

        public override byte[] GetBytes()
        {
            return Value;
        }

        public override string ToString()
        {
            var hex = BitConverter.ToString(Value).Replace("-", " ");
            return $"<B[{Value.Length}] {hex}>";
        }
    }

    // SECS Boolean Item
    public class SecsBooleanItem : SecsItem
    {
        public override SecsFormat Format => SecsFormat.Boolean;
        public byte[] Value { get; set; }

        public SecsBooleanItem(byte[] value)
        {
            Value = value ?? new byte[0];
        }

        public override byte[] GetBytes()
        {
            return Value;
        }

        public override string ToString()
        {
            var boolStr = string.Join(" ", Value.Select(b => b != 0 ? "T" : "F"));
            return $"<BOOLEAN[{Value.Length}] {boolStr}>";
        }
    }

    // SECS JIS8 Item
    public class SecsJis8Item : SecsItem
    {
        public override SecsFormat Format => SecsFormat.JIS8;
        public byte[] Value { get; set; }

        public SecsJis8Item(byte[] value)
        {
            Value = value ?? new byte[0];
        }

        public override byte[] GetBytes()
        {
            return Value;
        }

        public override string ToString()
        {
            return $"<J[{Value.Length}]>";
        }
    }

    // SECS Integer Items
    public class SecsI1Item : SecsItem
    {
        public override SecsFormat Format => SecsFormat.I1;
        public byte[] Value { get; set; }

        public SecsI1Item(byte[] value)
        {
            Value = value ?? new byte[0];
        }

        public override byte[] GetBytes()
        {
            return Value;
        }

        public override string ToString()
        {
            var values = Value.Select(b => ((sbyte)b).ToString()).ToArray();
            return $"<I1[{Value.Length}] {string.Join(" ", values)}>";
        }
    }

    public class SecsI2Item : SecsItem
    {
        public override SecsFormat Format => SecsFormat.I2;
        public byte[] Value { get; set; }

        public SecsI2Item(byte[] value)
        {
            Value = value ?? new byte[0];
        }

        public override byte[] GetBytes()
        {
            return Value;
        }

        public override string ToString()
        {
            var values = new List<string>();
            for (int i = 0; i < Value.Length; i += 2)
            {
                if (i + 1 < Value.Length)
                {
                    var val = BitConverter.ToInt16(new byte[] { Value[i], Value[i + 1] }, 0);
                    values.Add(val.ToString());
                }
            }
            return $"<I2[{Value.Length / 2}] {string.Join(" ", values)}>";
        }
    }

    public class SecsI4Item : SecsItem
    {
        public override SecsFormat Format => SecsFormat.I4;
        public byte[] Value { get; set; }

        public SecsI4Item(byte[] value)
        {
            Value = value ?? new byte[0];
        }

        public override byte[] GetBytes()
        {
            return Value;
        }

        public override string ToString()
        {
            var values = new List<string>();
            for (int i = 0; i < Value.Length; i += 4)
            {
                if (i + 3 < Value.Length)
                {
                    var val = BitConverter.ToInt32(new byte[] { Value[i], Value[i + 1], Value[i + 2], Value[i + 3] }, 0);
                    values.Add(val.ToString());
                }
            }
            return $"<I4[{Value.Length / 4}] {string.Join(" ", values)}>";
        }
    }

    public class SecsI8Item : SecsItem
    {
        public override SecsFormat Format => SecsFormat.I8;
        public byte[] Value { get; set; }

        public SecsI8Item(byte[] value)
        {
            Value = value ?? new byte[0];
        }

        public override byte[] GetBytes()
        {
            return Value;
        }

        public override string ToString()
        {
            var values = new List<string>();
            for (int i = 0; i < Value.Length; i += 8)
            {
                if (i + 7 < Value.Length)
                {
                    var val = BitConverter.ToInt64(new byte[] { Value[i], Value[i + 1], Value[i + 2], Value[i + 3],
                                                               Value[i + 4], Value[i + 5], Value[i + 6], Value[i + 7] }, 0);
                    values.Add(val.ToString());
                }
            }
            return $"<I8[{Value.Length / 8}] {string.Join(" ", values)}>";
        }
    }

    // SECS Unsigned Integer Items
    public class SecsU1Item : SecsItem
    {
        public override SecsFormat Format => SecsFormat.U1;
        public byte[] Value { get; set; }

        public SecsU1Item(byte[] value)
        {
            Value = value ?? new byte[0];
        }

        public override byte[] GetBytes()
        {
            return Value;
        }

        public override string ToString()
        {
            var values = Value.Select(b => b.ToString()).ToArray();
            return $"<U1[{Value.Length}] {string.Join(" ", values)}>";
        }
    }

    public class SecsU2Item : SecsItem
    {
        public override SecsFormat Format => SecsFormat.U2;
        public byte[] Value { get; set; }

        public SecsU2Item(byte[] value)
        {
            Value = value ?? new byte[0];
        }

        public override byte[] GetBytes()
        {
            return Value;
        }

        public override string ToString()
        {
            var values = new List<string>();
            for (int i = 0; i < Value.Length; i += 2)
            {
                if (i + 1 < Value.Length)
                {
                    var val = BitConverter.ToUInt16(new byte[] { Value[i], Value[i + 1] }, 0);
                    values.Add(val.ToString());
                }
            }
            return $"<U2[{Value.Length / 2}] {string.Join(" ", values)}>";
        }
    }

    public class SecsU4Item : SecsItem
    {
        public override SecsFormat Format => SecsFormat.U4;
        public uint Value { get; set; }

        public SecsU4Item(uint value)
        {
            Value = value;
        }

        public override byte[] GetBytes()
        {
            var bytes = BitConverter.GetBytes(Value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return bytes;
        }

        public override string ToString()
        {
            return $"<U4[1] {Value}>";
        }
    }

    public class SecsU8Item : SecsItem
    {
        public override SecsFormat Format => SecsFormat.U8;
        public byte[] Value { get; set; }

        public SecsU8Item(byte[] value)
        {
            Value = value ?? new byte[0];
        }

        public override byte[] GetBytes()
        {
            return Value;
        }

        public override string ToString()
        {
            var values = new List<string>();
            for (int i = 0; i < Value.Length; i += 8)
            {
                if (i + 7 < Value.Length)
                {
                    var val = BitConverter.ToUInt64(new byte[] { Value[i], Value[i + 1], Value[i + 2], Value[i + 3],
                                                                Value[i + 4], Value[i + 5], Value[i + 6], Value[i + 7] }, 0);
                    values.Add(val.ToString());
                }
            }
            return $"<U8[{Value.Length / 8}] {string.Join(" ", values)}>";
        }
    }

    // SECS Float Items
    public class SecsF4Item : SecsItem
    {
        public override SecsFormat Format => SecsFormat.F4;
        public float Value { get; set; }

        public SecsF4Item(float value)
        {
            Value = value;
        }

        public override byte[] GetBytes()
        {
            var bytes = BitConverter.GetBytes(Value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return bytes;
        }

        public override string ToString()
        {
            return $"<F4[1] {Value:F6}>";
        }
    }

    public class SecsF8Item : SecsItem
    {
        public override SecsFormat Format => SecsFormat.F8;
        public double Value { get; set; }

        public SecsF8Item(double value)
        {
            Value = value;
        }

        public override byte[] GetBytes()
        {
            var bytes = BitConverter.GetBytes(Value);
            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return bytes;
        }

        public override string ToString()
        {
            return $"<F8[1] {Value:F6}>";
        }
    }

    // Event Arguments
    public class MessageProcessedEventArgs : EventArgs
    {
        public string ProcessingResult { get; }

        public MessageProcessedEventArgs(string result)
        {
            ProcessingResult = result;
        }
    }
}