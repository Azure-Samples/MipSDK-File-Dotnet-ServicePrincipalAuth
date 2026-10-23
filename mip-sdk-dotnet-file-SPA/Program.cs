// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using MipSdkDotNetQuickstart;

return await SampleApplication.RunAsync(args, Console.In, Console.Out, Console.Error, () =>
{
    var configuration = SampleSettings.LoadConfiguration();
    using var configurationLifetime = configuration as IDisposable;
    return MipHandler.CreateAsync(SampleSettings.Load(configuration));
});
