using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace SHINE.Core
{
    public class Message : IMessage
    {
        public int OpCode { get; set; }
        public string SenderId { get; set; } = string.Empty;
        public string RecipientId { get; set; } = string.Empty;
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public byte[] DataRaw { get; set; } = Array.Empty<byte>();

        public byte[] Payload
        {
            get => Data;
            set => Data = value;
        }

        private static readonly Dictionary<Type, Func<object?, string?>> ToStrCache = new Dictionary<Type, Func<object?, string?>>();
        private static readonly Dictionary<Type, Func<string, object?>> FromStrCache = new Dictionary<Type, Func<string, object?>>();
        private static readonly Dictionary<Type, PropertyInfo[]> PropsCache = new Dictionary<Type, PropertyInfo[]>();
        private static readonly Dictionary<Type, Type> EnumBaseCache = new Dictionary<Type, Type>();

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms, Encoding.UTF8, true);

            bw.Write(OpCode);
            bw.Write(SenderId);
            bw.Write(RecipientId);
            bw.Write(Data.Length);
            bw.Write(Data);
            bw.Write(DataRaw.Length);
            bw.Write(DataRaw);

            return ms.ToArray();
        }

        public void Deserialize(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var br = new BinaryReader(ms, Encoding.UTF8);

            OpCode = br.ReadInt32();
            SenderId = br.ReadString();
            RecipientId = br.ReadString();
            Data = br.ReadBytes(br.ReadInt32());
            DataRaw = br.ReadBytes(br.ReadInt32());
        }

        public T GetData<T>()
        {
            using var ms = new MemoryStream(Data);
            using var br = new BinaryReader(ms, Encoding.UTF8);
            return (T)ReadAny(typeof(T), br);
        }

        public static MessageBuilder Create() => new MessageBuilder();

        public class MessageBuilder
        {
            private readonly Message _msg = new Message();

            public MessageBuilder WithOpCode(int code) { _msg.OpCode = code; return this; }
            public MessageBuilder From(string sender) { _msg.SenderId = sender; return this; }
            public MessageBuilder To(string recipient) { _msg.RecipientId = recipient; return this; }
            public MessageBuilder Data<T>(T obj) { _msg.Data = SerializeAny(obj, typeof(T)); return this; }
            public MessageBuilder DataRaw(byte[] raw) { _msg.DataRaw = raw; return this; }
            public Message Build() => _msg;
        }

        private static byte[] SerializeAny(object? obj, Type type)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms, Encoding.UTF8, true);
            WriteAny(obj, type, bw);
            return ms.ToArray();
        }

        private static void WriteAny(object? obj, Type type, BinaryWriter bw)
        {

            if (type.IsEnum)
            {
                var baseType = GetEnumBaseType(type);
                WriteAny(Convert.ChangeType(obj, baseType), baseType, bw);
                return;
            }

            if (type == typeof(string))
            {
                if (obj == null)
                {
                    bw.Write(-1);
                    return;
                }
                var bytes = Encoding.UTF8.GetBytes((string)obj);
                bw.Write(bytes.Length);
                bw.Write(bytes);
                return;
            }

            if (WritePrimitive(obj, type, bw)) return;

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                if (obj == null)
                {
                    bw.Write(false);
                    return;
                }
                bw.Write(true);
                WriteAny(obj, Nullable.GetUnderlyingType(type), bw);
                return;
            }

            if (!type.IsValueType)
            {
                if (obj == null)
                {
                    bw.Write(false);
                    return;
                }
                bw.Write(true);
            }

            if (IsValueTuple(type))
            {
                var fields = type.GetFields().OrderBy(f => f.Name).ToArray();
                foreach (var field in fields)
                    WriteAny(field.GetValue(obj), field.FieldType, bw);
                return;
            }

            if (typeof(IDictionary).IsAssignableFrom(type))
            {
                var dict = (IDictionary)obj!;
                bw.Write(dict.Count);
                var args = type.GetGenericArguments();
                foreach (DictionaryEntry entry in dict)
                {
                    WriteAny(entry.Key, args[0], bw);
                    WriteAny(entry.Value, args[1], bw);
                }
                return;
            }

            if (typeof(IEnumerable).IsAssignableFrom(type))
            {
                var enumerable = (IEnumerable)obj!;
                int count;
                if (obj is ICollection coll)
                {
                    count = coll.Count;
                }
                else
                {
                    count = 0;
                    foreach (var _ in enumerable) count++;
                }
                bw.Write(count);

                var elemType = GetEnumerableElementType(type);
                foreach (var item in enumerable)
                    WriteAny(item, elemType, bw);
                return;
            }

            foreach (var prop in GetProps(type))
                WriteAny(prop.GetValue(obj), prop.PropertyType, bw);
        }

        private static object ReadAny(Type type, BinaryReader br)
        {

            if (type.IsEnum)
            {
                var baseType = GetEnumBaseType(type);
                return Enum.ToObject(type, ReadAny(baseType, br));
            }

            if (type == typeof(string))
            {
                int len = br.ReadInt32();
                return len == -1 ? null : Encoding.UTF8.GetString(br.ReadBytes(len));
            }

            var primitive = ReadPrimitive(type, br);
            if (primitive.Item1) return primitive.Item2!;

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                return br.ReadBoolean() ? ReadAny(Nullable.GetUnderlyingType(type), br) : null;
            }

            if (!type.IsValueType && !br.ReadBoolean()) return null;

            if (IsValueTuple(type))
            {
                var fields = type.GetFields().OrderBy(f => f.Name).ToArray();
                var values = new object[fields.Length];
                for (int i = 0; i < fields.Length; i++)
                    values[i] = ReadAny(fields[i].FieldType, br);
                return Activator.CreateInstance(type, values);
            }

            if (typeof(IDictionary).IsAssignableFrom(type))
            {
                int count = br.ReadInt32();
                var args = type.GetGenericArguments();
                var dict = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(args));
                for (int i = 0; i < count; i++)
                {
                    var key = ReadAny(args[0], br);
                    var value = ReadAny(args[1], br);
                    dict.Add(key, value);
                }
                return dict;
            }

            if (typeof(IEnumerable).IsAssignableFrom(type))
            {
                int count = br.ReadInt32();
                var elemType = GetEnumerableElementType(type);
                var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elemType));

                for (int i = 0; i < count; i++)
                    list.Add(ReadAny(elemType, br));

                if (type.IsArray)
                {
                    var arr = Array.CreateInstance(elemType, count);
                    list.CopyTo(arr, 0);
                    return arr;
                }
                return list;
            }

            var instance = Activator.CreateInstance(type);
            foreach (var prop in GetProps(type))
                prop.SetValue(instance, ReadAny(prop.PropertyType, br));
            return instance;
        }

        private static bool WritePrimitive(object obj, Type type, BinaryWriter bw)
        {
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean: bw.Write((bool)obj); return true;
                case TypeCode.Byte: bw.Write((byte)obj); return true;
                case TypeCode.SByte: bw.Write((sbyte)obj); return true;
                case TypeCode.Int16: bw.Write((short)obj); return true;
                case TypeCode.UInt16: bw.Write((ushort)obj); return true;
                case TypeCode.Int32: bw.Write((int)obj); return true;
                case TypeCode.UInt32: bw.Write((uint)obj); return true;
                case TypeCode.Int64: bw.Write((long)obj); return true;
                case TypeCode.UInt64: bw.Write((ulong)obj); return true;
                case TypeCode.Single: bw.Write((float)obj); return true;
                case TypeCode.Double: bw.Write((double)obj); return true;
            }

            if (type == typeof(decimal))
            {
                var bits = decimal.GetBits((decimal)obj);
                bw.Write(bits[0]); bw.Write(bits[1]); bw.Write(bits[2]); bw.Write(bits[3]);
                return true;
            }

            if (type == typeof(DateTime))
            {
                bw.Write(((DateTime)obj).Ticks);
                return true;
            }

            if (type == typeof(TimeSpan))
            {
                bw.Write(((TimeSpan)obj).Ticks);
                return true;
            }

            if (type == typeof(Guid))
            {
                bw.Write(((Guid)obj).ToByteArray());
                return true;
            }

            return false;
        }

        private static (bool, object?) ReadPrimitive(Type type, BinaryReader br)
        {
            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean: return (true, br.ReadBoolean());
                case TypeCode.Byte: return (true, br.ReadByte());
                case TypeCode.SByte: return (true, br.ReadSByte());
                case TypeCode.Int16: return (true, br.ReadInt16());
                case TypeCode.UInt16: return (true, br.ReadUInt16());
                case TypeCode.Int32: return (true, br.ReadInt32());
                case TypeCode.UInt32: return (true, br.ReadUInt32());
                case TypeCode.Int64: return (true, br.ReadInt64());
                case TypeCode.UInt64: return (true, br.ReadUInt64());
                case TypeCode.Single: return (true, br.ReadSingle());
                case TypeCode.Double: return (true, br.ReadDouble());
            }

            if (type == typeof(decimal))
                return (true, new decimal(new[] { br.ReadInt32(), br.ReadInt32(), br.ReadInt32(), br.ReadInt32() }));

            if (type == typeof(DateTime))
                return (true, new DateTime(br.ReadInt64()));

            if (type == typeof(TimeSpan))
                return (true, new TimeSpan(br.ReadInt64()));

            if (type == typeof(Guid))
                return (true, new Guid(br.ReadBytes(16)));

            return (false, null);
        }

        private static bool IsValueTuple(Type type) =>
            type.FullName?.StartsWith("System.ValueTuple`") == true;

        private static Type GetEnumBaseType(Type enumType)
        {
            if (!EnumBaseCache.TryGetValue(enumType, out var baseType))
            {
                baseType = Enum.GetUnderlyingType(enumType);
                EnumBaseCache[enumType] = baseType;
            }
            return baseType;
        }

        private static Type GetEnumerableElementType(Type collectionType)
        {
            if (collectionType.IsArray)
                return collectionType.GetElementType();

            if (collectionType.IsGenericType && collectionType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return collectionType.GetGenericArguments()[0];

            var ienum = collectionType.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            return ienum?.GetGenericArguments()[0] ?? typeof(object);
        }

        private static PropertyInfo[] GetProps(Type type)
        {
            if (!PropsCache.TryGetValue(type, out var props))
            {
                props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .OrderBy(p => p.Name)
                    .ToArray();
                PropsCache[type] = props;
            }
            return props;
        }

        private static Func<object?, string?> GetToStringConverter(Type type)
        {
            if (ToStrCache.TryGetValue(type, out var f)) return f;

            var param = Expression.Parameter(typeof(object));
            var cast = Expression.Convert(param, type);
            var toStr = Expression.Call(cast, type.GetMethod("ToString", Type.EmptyTypes)!);
            var condition = Expression.Condition(
                Expression.Equal(param, Expression.Constant(null)),
                Expression.Constant(null, typeof(string)),
                toStr
            );
            var lambda = Expression.Lambda<Func<object?, string?>>(condition, param);
            var compiled = lambda.Compile();
            ToStrCache[type] = compiled;
            return compiled;
        }

        private static Func<string, object?> GetFromStringConverter(Type type)
        {
            if (FromStrCache.TryGetValue(type, out var f)) return f;

            var param = Expression.Parameter(typeof(string));
            Expression body;

            var parse = type.GetMethod("Parse", new[] { typeof(string) });
            if (parse != null)
                body = Expression.Call(parse, param);
            else
                body = Expression.Convert(
                    Expression.Call(typeof(Convert), nameof(Convert.ChangeType), null, param, Expression.Constant(type)),
                    typeof(object)
                );

            var lambda = Expression.Lambda<Func<string, object?>>(body, param);
            var compiled = lambda.Compile();
            FromStrCache[type] = compiled;
            return compiled;
        }
    }
}