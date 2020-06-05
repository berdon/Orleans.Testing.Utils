using Serilog.Core;
using Serilog.Events;
using System;
using Xunit.Abstractions;

namespace Orleans.Testing.Utils
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
