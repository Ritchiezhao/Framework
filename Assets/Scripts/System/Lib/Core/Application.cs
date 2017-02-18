﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using CatLib.Contracts;
using CatLib.Contracts.Event;

namespace CatLib
{

    /// <summary>
    /// CatLib程序
    /// </summary>
    public class Application : Container.Container, IApplication
    {

        /// <summary>
        /// CatLib框架版本
        /// </summary>
        public const string VERSION = "0.0.1";

        /// <summary>
        /// 框架启动流程
        /// </summary>
        public enum StartProcess
        {

            /// <summary>
            /// 引导流程
            /// </summary>
            ON_BOOTSTRAP = 1,

            /// <summary>
            /// 依赖检测流程
            /// </summary>
            ON_DEPEND = 3,

            /// <summary>
            /// 初始化流程
            /// </summary>
            ON_INITED = 4,

            /// <summary>
            /// 服务提供商启动流程
            /// </summary>
            ON_PROVIDER_PROCESS = 5,

            /// <summary>
            /// 启动完成
            /// </summary>
            ON_COMPLETE = 6,

        }

        /// <summary>
        /// 服务提供商
        /// </summary>
        protected Dictionary<Type, ServiceProvider> serviceProviders = new Dictionary<Type, ServiceProvider>();

        /// <summary>
        /// 更新
        /// </summary>
        protected List<IUpdate> update = new List<IUpdate>();

        /// <summary>
        /// 延后更新
        /// </summary>
        protected List<ILateUpdate> lateUpdate = new List<ILateUpdate>();

        /// <summary>
        /// 释放时需要调用的
        /// </summary>
        protected List<IDestroy> destroy = new List<IDestroy>();

        /// <summary>
        /// 是否已经完成引导程序
        /// </summary>
        protected bool bootstrapped = false;

        /// <summary>
        /// 是否已经完成初始化
        /// </summary>
        protected bool inited = false;

        /// <summary>
        /// 启动流程
        /// </summary>
        protected StartProcess process = StartProcess.ON_BOOTSTRAP;

        /// <summary>
        /// 全局唯一自增
        /// </summary>
        protected long guid = 0;

        /// <summary>
        /// 主线程ID
        /// </summary>
        private int mainThreadID;

        /// <summary>
        /// 调度队列
        /// </summary>
        private Queue<object[]> actionQueue = new Queue<object[]>();

        /// <summary>
        /// 调度队列锁
        /// </summary>
        private object actionLocker = new object();

        public Application()
        {

            mainThreadID = System.Threading.Thread.CurrentThread.ManagedThreadId;

            Decorator((container, bindData, obj) =>
            {

                if (bindData.IsStatic)
                {
                    if (obj is IUpdate)
                    {
                        update.Add(obj as IUpdate);
                    }

                    if (obj is ILateUpdate)
                    {
                        lateUpdate.Add(obj as ILateUpdate);
                    }
                    if (obj is IDestroy)
                    {
                        destroy.Add(obj as IDestroy);
                    }
                }
                return obj;
            });
        }


        /// <summary>
        /// 引导程序
        /// </summary>
        /// <param name="bootstraps">引导文件</param>
        /// <returns></returns>
        public IApplication Bootstrap(Type[] bootstraps)
        {
            process = StartProcess.ON_BOOTSTRAP;

            App.Instance = this;
            Instances(typeof(Application).ToString(), this);
            Alias(typeof(IApplication).ToString(), typeof(Application).ToString());
            Alias(typeof(App).ToString(), typeof(Application).ToString());

            IBootstrap bootstrap;
            foreach (Type t in bootstraps)
            {
                bootstrap = this.Make<IBootstrap>(t);
                if (bootstrap != null)
                {
                    bootstrap.Bootstrap();
                }
            }

            bootstrapped = true;

            return this;
        }

        /// <summary>
        /// 初始化
        /// </summary>
        /// <param name="provider"></param>
        public void Init()
        {
            if (inited) { return; }
            if (!bootstrapped) { return; }

            ServiceProvider[] providers = serviceProviders.ToArray();

            process = StartProcess.ON_DEPEND;

            Event.Trigger(ApplicationEvents.ON_DEPENDING);

            foreach (ServiceProvider provider in providers)
            {

                foreach (Type type in provider.ProviderDepend)
                {

                    if (!HasDepend(type.ToString()))
                    {
                        throw new Exception("service provider [" + provider.GetType().ToString() + "] depend service provider [" + type.ToString() + "]");
                    }
                }

            }

            Event.Trigger(ApplicationEvents.ON_DEPENDED);

            process = StartProcess.ON_INITED;

            Event.Trigger(ApplicationEvents.ON_INITING);

            foreach (ServiceProvider serviceProvider in providers)
            {
                serviceProvider.Init();
            }

            inited = true;

            Event.Trigger(ApplicationEvents.ON_INITED);

            StartCoroutine(StartProviderPorcess());

        }

        /// <summary>
        /// 注册服务提供者
        /// </summary>
        /// <param name="t"></param>
        public void Register(Type t)
        {
            if (this.serviceProviders.ContainsKey(t)) { return; }

            ServiceProvider serviceProvider = this.Make<ServiceProvider>(t);
            if (serviceProvider != null)
            {
                serviceProvider.Register();
                this.serviceProviders.Add(t, serviceProvider);
                if (this.inited) { serviceProvider.Init(); }
            }

        }

        public void Update()
        {
            for (int i = 0; i < update.Count; i++)
            {
                update[i].Update();
            }
            lock (actionLocker)
            {
                while (actionQueue.Count > 0)
                {
                    CallAction(actionQueue.Dequeue());
                }
            }
        }

        public void LateUpdate()
        {
            for (int i = 0; i < lateUpdate.Count; i++)
            {
                lateUpdate[i].LateUpdate();
            }
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            for (int i = 0; i < destroy.Count; i++)
            {
                destroy[i].OnDestroy();
            }
        }

        /// <summary>
        /// 获取一个唯一id
        /// </summary>
        /// <returns></returns>
        public long GetGuid()
        {
            return Interlocked.Increment(ref guid);
        }

        /// <summary>
        /// 启动服务提供商启动流程
        /// </summary>
        /// <returns></returns>
        protected IEnumerator StartProviderPorcess()
        {

            process = StartProcess.ON_PROVIDER_PROCESS;

            Trigger(ApplicationEvents.ON_PROVIDER_PROCESSING);

            List<ServiceProvider> providers = new List<ServiceProvider>(serviceProviders.Values);
            providers.Sort((left, right) => ((int)left.ProviderProcess).CompareTo((int)right.ProviderProcess) );

            foreach(ServiceProvider provider in providers)
            {
                yield return provider.OnProviderProcess();
            }

            Trigger(ApplicationEvents.ON_PROVIDER_PROCESSED);

            process = StartProcess.ON_COMPLETE;

            Trigger(ApplicationEvents.ON_APPLICATION_START_COMPLETE);

        }

        #region Dispatcher

        public override IEventAchieve Event
        {
            get
            {
                return this;
            }
        }

        public void Trigger(string eventName)
        {

            Trigger(eventName, null, EventArgs.Empty);

        }

        public void Trigger(string eventName, EventArgs e)
        {

            Trigger(eventName, null, e);

        }

        public void Trigger(string eventName, object sender)
        {

            Trigger(eventName, sender, EventArgs.Empty);

        }

        public void Trigger(string eventName, object sender, EventArgs e)
        {

            if (mainThreadID == System.Threading.Thread.CurrentThread.ManagedThreadId)
            {

                base.Event.Trigger(eventName, sender, e);
                return;

            }

            lock (actionLocker)
            {
 
                actionQueue.Enqueue(new object[] { "trigger", eventName, sender, e });

            }

        }

        public void On(string eventName, EventHandler handler)
        {

            base.Event.On(eventName, handler);

        }

        public void One(string eventName, EventHandler handler)
        {

            base.Event.One(eventName, handler);

        }

        public void Off(string eventName, EventHandler handler)
        {

            base.Event.Off(eventName, handler);

        }

        public void OffOne(string eventName, EventHandler handler)
        {

            base.Event.OffOne(eventName, handler);

        }

        private void CallAction(object[] data)
        {

            switch (data[0] as string)
            {
                case "trigger": base.Event.Trigger(data[1] as string, data[2], data[3] as EventArgs); break;
                default: break;
            }

        }

        #endregion


    }

}