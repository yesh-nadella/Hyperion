﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Wire.ValueSerializers;

namespace Wire.SerializerFactories
{
    public class EnumerableSerializerFactory : ValueSerializerFactory
    {
        public override bool CanSerialize(Serializer serializer, Type type)
        {
            //TODO: check for constructor with IEnumerable<T> param

            if (type.GetTypeInfo().GetMethod("AddRange") == null && type.GetTypeInfo().GetMethod("Add") == null)
                return false;

            if (type.GetTypeInfo().GetProperty("Count") == null)
                return false;

            var isGenericEnumerable = GetEnumerableType(type) != null;
            if (isGenericEnumerable)
                return true;

            if (typeof (ICollection).GetTypeInfo().IsAssignableFrom(type))
                return true;

            return false;
        }

        public override bool CanDeserialize(Serializer serializer, Type type)
        {
            return CanSerialize(serializer, type);
        }

        private static Type GetEnumerableType(Type type)
        {
            return type
                .GetTypeInfo()
                .GetInterfaces()
                .Where(intType => intType.GetTypeInfo().IsGenericType && intType.GetTypeInfo().GetGenericTypeDefinition() == typeof (IEnumerable<>))
                .Select(intType => intType.GetTypeInfo().GetGenericArguments()[0])
                .FirstOrDefault();
        }

        public override ValueSerializer BuildSerializer(Serializer serializer, Type type,
            ConcurrentDictionary<Type, ValueSerializer> typeMapping)
        {
            var x = new ObjectSerializer(type);
            typeMapping.TryAdd(type, x);
            var preserveObjectReferences = serializer.Options.PreserveObjectReferences;

            var elementType = GetEnumerableType(type) ?? typeof (object);
            var elementSerializer = serializer.GetSerializerByType(elementType);

            var countProperty = type.GetTypeInfo().GetProperty("Count");
            var addRange = type.GetTypeInfo().GetMethod("AddRange");
            var add = type.GetTypeInfo().GetMethod("Add");

            Func<object, int> countGetter = o => (int)countProperty.GetValue(o);


            ObjectReader reader = (stream, session) =>
            {
                var instance = Activator.CreateInstance(type);
                if (preserveObjectReferences)
                {
                    session.TrackDeserializedObject(instance);
                }


                var count = stream.ReadInt32(session);
                var items = Array.CreateInstance(elementType, count);

                for (var i = 0; i < count; i++)
                {
                    var value = stream.ReadObject(session);
                    items.SetValue(value, i);
                }
                //HACK: this needs to be fixed, codegenerated or whatever

                if (addRange != null)
                {
                    addRange.Invoke(instance, new object[] {items});
                    return instance;
                }
                if (add != null)
                {
                    for (var i = 0; i < items.Length; i++)
                    {
                        add.Invoke(instance, new[] {items.GetValue(i)});
                    }
                }


                return instance;
            };



            ObjectWriter writer = (stream, o, session) =>
            {
                if (preserveObjectReferences)
                {
                    session.TrackSerializedObject(o);
                }

                stream.WriteInt32(countGetter(o));
                var enumerable = o as IEnumerable;
                // ReSharper disable once PossibleNullReferenceException
                foreach (var value in enumerable)
                {
                    stream.WriteObject(value, elementType, elementSerializer, preserveObjectReferences, session);
                }


            };
            x.Initialize(reader, writer);
            return x;
        }
    }
}