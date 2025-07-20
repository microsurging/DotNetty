# DotNetty Project
[DotNetty](https://github.com/Azure/DotNetty) is a port of [Netty](https://github.com/netty/netty), This project is derived from the SpanNetty project and continues to be improved, asynchronous event-driven network application framework for rapid development of maintainable high performance protocol servers & clients.
## Use
Default TaskScheduler
``` 
                _bossEventExecutor = new MultithreadEventLoopGroup(1);
                _workEventExecutor = new MultithreadEventLoopGroup();

//Libuv
                var dispatcher = new DispatcherEventLoopGroup();
                _bossEventExecutor = dispatcher;
                _workEventExecutor = new WorkerEventLoopGroup(dispatcher);
            } 
```

Alone TaskScheduler
``` 
                         _bossEventExecutor = new MultithreadEventLoopGroup(1,TaskSchedulerType.Alone);
               _workEventExecutor = new MultithreadEventLoopGroup(TaskSchedulerType.Alone);

//Libuv
                var dispatcher = new DispatcherEventLoopGroup(TaskSchedulerType.Alone);
                _bossEventExecutor = dispatcher;
                _workEventExecutor = new WorkerEventLoopGroup(dispatcher,TaskSchedulerType.Alone);
            } 
```

The Alone TaskScheduler is used to handle time-consuming task scheduling and requires manual configuration 
