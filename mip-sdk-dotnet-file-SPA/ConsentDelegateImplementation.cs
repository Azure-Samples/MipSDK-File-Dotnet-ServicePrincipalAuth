// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.InformationProtection;

namespace MipSdkDotNetQuickstart;

/// <summary>
/// Uses the operator's explicit service-consent decision for unattended execution.
/// </summary>
internal sealed class ConsentDelegateImplementation(bool accepted) : IConsentDelegate
{
    /// <summary>
    /// Returns consent for the specified URL.
    /// </summary>
    public Consent GetUserConsent(string url) => accepted ? Consent.Accept : Consent.Reject;
}
