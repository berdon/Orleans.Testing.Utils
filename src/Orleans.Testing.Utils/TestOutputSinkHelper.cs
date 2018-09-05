using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Text;
using Xunit.Abstractions;

namespace SharedOrleansUtils
{
    public class TestOutputHelperSink : ILogEventSink
    {
        private readonly IFormatProvider _formatProvider;
        private readonly ITestOutputHelper _outputHelper;

        public TestOutputHelperSink(IFormatProvider formatProvider, ITestOutputHelper outputHelper)
        {
            _formatProvider = formatProvider;
            _outputHelper = outputHelper;
        }

        public void Emit(LogEvent logEvent)
        {
            _outputHelper.WriteLine(logEvent.RenderMessage());
        }
    }
}
