---
page_type: sample
languages:
- csharp
products:
- azure
description: "This sample application demonstrates using the Microsoft Information Protection SDK .NET wrapper to label and read a label from a file using service principal authentication. "
urlFragment: MipSdk-Dotnet-File-ServicePrincipalAuth
---

# MipSdk-Dotnet-File-ServicePrincipalAuth

This sample application demonstrates using the Microsoft Information Protection SDK .NET wrapper to label and read a label from a file using **service principal authentication**. The sample provides steps and code or both client secret and certificate-based authentication.

Beyond the authentication flow, it demonstrates:

- Fetching labels for the tenant
- Applying a label to a file
- Reading a label from a file

## Summary

This sample application illustrates using the MIP File API to list labels, apply a label, then read the label as a service principal identity. All SDK actions are implemented in **action.cs**. All auth behaviour is implemented in **AuthDelegateImplementation.cs**.

## Getting Started

### Prerequisites

- Visual Studio 2015 or later with Visual C# development features installed

### Sample Setup

In Visual Studio 2017:

1. Right-click the project and select **Manage NuGet Packages**
2. On the **Browse** tab, search for *Microsoft.InformationProtection.File*
3. Select the package and click **Install**

### Create an Azure AD App Registration

Authentication against the Azure AD tenant requires creating a native application registration. The client ID created in this step is used in a later step to generate an OAuth2 token.

> Skip this step if you've already created a registration for previous sample. You may continue to use that client ID.

1. Go to https://portal.azure.com and log in as a global admin.
> Your tenant may permit standard users to register applications. If you aren't a global admin, you can attempt these steps, but may need to work with a tenant administrator to have an application registered or be granted access to register applications.
2. Click Azure Active Directory, then **App Registrations** in the menu blade.
3. Click **New Registration**
4. Under *Supported account types* select **Accounts in this directory only**
5. Under *Redirect URI* select **Public client**
6. For *Redirect URI*, enter **mipsdk-auth-sample://authorize**   
  > Note: This can be anything you'd like.
8. Click **Register**

The registered app should now be displayed.

<<<<<<< Updated upstream
1. Click **API permissions**.
2. Click **Add a permission**.
=======
### Add API Permissions 

1. Select **API permissions**.
2. Select **Add a permission**.
>>>>>>> Stashed changes
3. Select **Microsoft APIs**.
4. Select **Azure Rights Management Services**.
5. Select **Application permissions**.
6. Under **Select Permissions** select **Content.DelegatedWriter** and **Content.Writer**.
7. Select **Add permissions**.
8. Again, Select **Add a permission**.
9. Select **APIs my organization uses**.
10. In the search box, type **Microsoft Information Protection Sync Service** then select the service.
11. Select **Application permissions**.
12. Select **UnifiedPolicy.Tenant.Read**.
13. Select **Add permissions**.
14. In the **API permissions** blade, Select **Grant admin consent for <Your Tenant>** and confirm.

### Generate a client secret

If you'd prefer to use certificate based auth, skip ahead to [Generate a client certificate](#Generate-a-client-certificate).

1. In the Azure AD application regisration menu, find the application you registered.
2. Select **Certificates and secrets**.
3. Click **New client secret**.
4. For the description, enter "MIP SDK Test App".
5. Select **In 1 year** for expiration
  > This can be 1 year, 2 years, or never.
6. Click **Add**.

The secret will be displayed in the portal. **Copy the secret now, as it will disappear after page refresh.**

  > Storing client secrets in plaintext isn't a best practice

### Generate a client certificate

This step generates a self-signed certificate, writes the thumbprint to the console, then exports the certificate to a cert file. If you used a [client secret](#Generate-a-client-secret), skip ahead to [update application configuration settings](#Update-application-configuration-settings).

Run the following PowerShell script:

```powershell
mkdir c:\temp
cd c:\temp

#Generate the certificate
$cert = New-SelfSignedCertificate -Subject "CN=MipSdkFileApiDotNet" -CertStoreLocation "Cert:\CurrentUser\My"  -KeyExportPolicy Exportable -KeySpec Signature
$cert.Thumbprint
$certFile = (Get-ChildItem -Path Cert:\CurrentUser\my\$($cert.thumbprint))
Export-Certificate -cert $cert -FilePath cba.cer -Type:CERT

# take the CER file and upload to AAD App Registration portal
```

### Import the certificate to the application registration

In this step, the public certificate generated in the previous section will be imported to the application registration.

1. In the Azure AD application regisration menu, find the application you registered.
2. Select **Certificates and secrets**
3. Click **Upload certificate**
4. Browse to the CER file generated in the previous section, then click **Add**

The certificate will appear in the list, displaying the thumbprint and validity period.

### Update application configuration settings

1. In Visual Studio open **app.config**.
2. Replace **YOUR CLIENT ID** with the application ID copied from the AAD App Registration **Overview** blade.
3. Replace **YOUR REDIRECT URI** with the Redirect URI copied from the AAD App Registration **Authentication** blade.
4. Replace **YOUR APP NAME** with the friendly name for your application. This will appear in logging and AIP Analytics.
5. Replace **YOUR APP VERSION** with the version of your application. This will appear in logging and AIP Analytics.
6. If you set the application to use *client secret* for auth, change **YOUR CLIENT SECRET** to the secret you copied earlier from **Certificates & secrets** and set **DoCertAuth** to **false**.
7. If you intend to use a certificate, set change **YOUR CERTIFICATE THUMBPRINT** to the thumbprint of the certificate displayed in the **Certificates & secrets** section and set **DoCertAuth** to **true**.
8. Replace **YOUR TENANT NAME** with the name of your Azure Active Directory Tenant (i.e. Contoso.com, or Contoso.onmicrosoft.com)

```xml
  <appSettings>
    <add key="ida:ClientId" value="YOUR CLIENT ID" />
    <add key="ida:RedirectUri" value="YOUR REDIRECT URI FROM AAD APP REGISTRATION" />
    <add key="ida:CertThumbprint" value="YOUR CERTIFICATE THUMBPRINT" />
    <add key="ida:ClientSecret" value="YOUR CLIENT SECRET"/>
    <add key="ida:DoCertAuth" value="false"/>
    <add key="ida:Tenant" value="YOUR TENANT NAME"/>
    <add key="app:Name" value="Test App" />
    <add key="app:Version" value="1.0.0" />
  </appSettings>
```

## Run the Sample

Press F5 to run the sample. The console application will start and after a brief moment displays the labels available for the user.

- Copy a label ID to the clipboard.
- Paste the label in to the input prompt.
- Next, the app asks for a path to a file. Enter the path to an Office document or PDF file.
- Finally, the app will display the name of the applied label.
- Attempt to open the file in a viewer that supports labeling or protection (Office or Adobe Reader)

## Resources

- [Microsoft Information Protection Docs](https://aka.ms/mipsdkdocs)
