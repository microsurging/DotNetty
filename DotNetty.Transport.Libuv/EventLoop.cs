﻿/*
 * Copyright 2012 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 * Copyright (c) The DotNetty Project (Microsoft). All rights reserved.
 *
 *   https://github.com/azure/dotnetty
 *
 * Licensed under the MIT license. See LICENSE file in the project root for full license information.
 *
 * Copyright (c) 2020 The Dotnetty-Span-Fork Project (cuteant@outlook.com) All rights reserved.
 *
 *   https://github.com/cuteant/dotnetty-span-fork
 *
 * Licensed under the MIT license. See LICENSE file in the project root for full license information.
 */

namespace DotNetty.Transport.Libuv
{
    using System;
    using DotNetty.Common.Concurrency;
    using DotNetty.Transport.Channels;

    public sealed class EventLoop : LoopExecutor
    {
        internal EventLoop(IEventLoopGroup parent, TaskSchedulerType taskSchedulerType)
            : this(parent, DefaultThreadFactory<EventLoop>.Instance, RejectedExecutionHandlers.Reject(), DefaultBreakoutInterval, taskSchedulerType)
        {
        }

        internal EventLoop(IEventLoopGroup parent)
           : this(parent, DefaultThreadFactory<EventLoop>.Instance, RejectedExecutionHandlers.Reject(), DefaultBreakoutInterval, TaskSchedulerType.Default)
        {
        }

        internal EventLoop(IEventLoopGroup parent, IThreadFactory threadFactory, IRejectedExecutionHandler rejectedHandler, TimeSpan breakoutInterval, TaskSchedulerType taskSchedulerType)
            : base(parent, threadFactory, rejectedHandler, breakoutInterval, taskSchedulerType)
        {
            Start();
        }
    }
}