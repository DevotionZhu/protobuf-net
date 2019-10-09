﻿using ProtoBuf.Compiler;
using ProtoBuf.Meta;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace ProtoBuf.Serializers
{
    internal sealed class RepeatedSerializerStub
    {
        internal static readonly RepeatedSerializerStub Empty = new RepeatedSerializerStub(null, null);
        public MemberInfo Provider { get; }
        public bool IsMap { get; }
        internal bool IsValidProtobufMap(RuntimeTypeModel model)
        {
            if (!IsMap) return false;
            ResolveMapTypes(out var key, out var value);

            // the key must an any integral or string type (not floating point or bytes)
            if (!IsValidKey(key)) return false;

            // the value cannot be repeated (neither can key, but we ruled that out above)
            var repeated = model == null ? RepeatedSerializers.TryGetRepeatedProvider(value) : model.TryGetRepeatedProvider(value);
            if (repeated != null) return false;

            return true;

            static bool IsValidKey(Type type)
            {
                if (type == null) return false;
                if (type.IsEnum) return true;
                if (type == typeof(string)) return true;
                if (!type.IsValueType) return false;
                if (Nullable.GetUnderlyingType(type) != null) return false;
                switch (Type.GetTypeCode(type))
                {
                    case TypeCode.SByte:
                    case TypeCode.Int16:
                    case TypeCode.Int32:
                    case TypeCode.Int64:
                    case TypeCode.Byte:
                    case TypeCode.UInt16:
                    case TypeCode.UInt32:
                    case TypeCode.UInt64:
                        return true;
                }
                return false;
            }
        }
        public bool IsEmpty => Provider == null;
        public object Serializer => _serializer ?? CreateSerializer();
        public Type ForType { get; }
        public Type ItemType { get; }
        private object _serializer;
        [MethodImpl(MethodImplOptions.NoInlining)]
        private object CreateSerializer()
        {
            var provider = RuntimeTypeModel.GetUnderlyingProvider(Provider, ForType);
            _serializer = provider switch
            {
                FieldInfo field when field.IsStatic => field.GetValue(null),
                MethodInfo method when method.IsStatic => method.Invoke(null, null),
                _ => null,
            };
            return _serializer;
        }

        internal void EmitProvider(CompilerContext ctx) => EmitProvider(ctx.IL);
        private void EmitProvider(ILGenerator il)
        {
            var provider = RuntimeTypeModel.GetUnderlyingProvider(Provider, ForType);
            RuntimeTypeModel.EmitProvider(provider, il);
        }

        public static RepeatedSerializerStub Create(Type forType, MemberInfo provider)
            => provider == null ? Empty : new RepeatedSerializerStub(forType, provider);

        private RepeatedSerializerStub(Type forType, MemberInfo provider)
        {
            ForType = forType;
            Provider = provider;
            IsMap = CheckIsMap(provider, out Type itemType);
            ItemType = itemType;
        }
        private static bool CheckIsMap(MemberInfo provider, out Type itemType)
        {
            var type = provider switch
            {
                MethodInfo method => method.ReturnType,
                FieldInfo field => field.FieldType,
                PropertyInfo prop => prop.PropertyType,
                Type t => t,
                _ => null,
            };
            while (type != null && type != typeof(object))
            {
                if (type.IsGenericType)
                {
                    var genDef = type.GetGenericTypeDefinition();
                    if (genDef == typeof(MapSerializer<,,>))
                    {
                        var targs = type.GetGenericArguments();
                        itemType = typeof(KeyValuePair<,>).MakeGenericType(targs[1], targs[2]);
                        return true;
                    }
                    if (genDef == typeof(RepeatedSerializer<,>))
                    {
                        var targs = type.GetGenericArguments();
                        itemType = targs[1];
                        return false;
                    }
                }

                type = type.BaseType;
            }
            itemType = null;
            return false;
        }

        internal void ResolveMapTypes(out Type keyType, out Type valueType)
        {
            keyType = valueType = null;
            if (IsMap)
            {
                var targs = ItemType.GetGenericArguments();
                keyType = targs[0];
                valueType = targs[1];
            }
        }
    }
}