﻿namespace ServiceControl.Plugin
{
    using System;
    using System.Collections.Generic;
    using NServiceBus;
    using NServiceBus.MessageInterfaces;

    class SimpleMessageMapper : IMessageMapper
    {
        T IMessageCreator.CreateInstance<T>()
        {
            return ((IMessageCreator) this).CreateInstance<T>(null);
        }

        T IMessageCreator.CreateInstance<T>(Action<T> action)
        {
            var result = (T) ((IMessageCreator) this).CreateInstance(typeof(T));
            action?.Invoke(result);
            return result;
        }

        object IMessageCreator.CreateInstance(Type messageType)
        {
            if (messageType.IsInterface || messageType.IsAbstract)
                throw new NotSupportedException("The binary serializer does not support interface types. Please use the XML serializer if you need this functionality.");

            return Activator.CreateInstance(messageType);
        }

        void IMessageMapper.Initialize(IEnumerable<Type> types)
        {
        }

        Type IMessageMapper.GetMappedTypeFor(Type t)
        {
            return t;
        }

        Type IMessageMapper.GetMappedTypeFor(string typeName)
        {
            return Type.GetType(typeName);
        }
    }
}