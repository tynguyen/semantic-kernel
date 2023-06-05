# Deploying Semantic Kernel to Azure in a web app service

This document details how to deploy Semantic Kernel as a backend service that can be used by other applications or by a frontend such as the one for [Copilot Chat](../../webapp/README.md).

## Things to know
- Access to Azure OpenAI is currently limited as we navigate high demand, upcoming product improvements, and Microsoft’s commitment to responsible AI. 
  For more details and information on applying for access, go [here](https://learn.microsoft.com/en-us/azure/cognitive-services/openai/overview?ocid=AID3051475#how-do-i-get-access-to-azure-openai).
  For regional availability of Azure OpenAI, see the [availability map](https://azure.microsoft.com/en-us/explore/global-infrastructure/products-by-region/?products=cognitive-services).
  
- Due to the limited availability of Azure OpenAI, consider using the same Azure OpenAI instance for multiple deployments of the Semantic Kernel web API and CopilotChat:
  - [Deploying with an existing Azure OpenAI account](#deploying-with-an-existing-azure-openai-account)
  - [Deploying with an existing OpenAI account](#deploying-with-an-existing-openai-account)

- F1 and D1 SKUs for the App Service Plans are not currently supported for this deployment.

- Using the templates and scripts below, only deploy one instance of Semantic Kernel to a given resource group. Also do not change the name of your deployment or its resources once deployed. The reason behind this restriction is that once virtual networks, subnets and applications are tied together, they need to be disentangled in the proper order before being deleted or modified, which is not something bicep or ARM templates can do. Consequently, deploying an alternate deployment within a resource group that already contains one will lead to resource conflicts.


# Deploying with a new Azure OpenAI instance
You can deploy an instance of Semantic Kernel in a web app service within a resource group that bears the name YOUR_DEPLOYMENT_NAME preceded by the "rg-" prefix using any of the following methods.

## PowerShell
Use the [DeploySK.ps1](DeploySK.ps1) file found in this folder:
```powershell
.\DeploySK.ps1 -DeploymentName YOUR_DEPLOYMENT_NAME -Subscription YOUR_SUBSCRIPTION_ID
```
For additional deployment options, see the deployment script.

## Bash
Use the [DeploySK.sh](DeploySK.sh) file found in this folder:
```bash
chmod +x ./DeploySK.sh
./DeploySK.sh -d DEPLOYMENT_NAME -s SUBSCRIPTION_ID
```

## Azure Portal
You can also deploy by clicking on:

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fmicrosoft%2Fsemantic-kernel%2Fmain%2Fsamples%2Fapps%2Fcopilot-chat-app%2Fwebapi%2FDeploymentTemplates%2Fsk-new.json)


# Deploying with an existing Azure OpenAI account
## PowerShell
Use the [DeploySK-Existing-AzureOpenAI.ps1](DeploySK-Existing-AzureOpenAI.ps1) file found in this folder:
```powershell
.\DeploySK-Existing-AzureOpenAI.ps1 -DeploymentName YOUR_DEPLOYMENT_NAME -Subscription YOUR_SUBSCRIPTION_ID -Endpoint YOUR_AZURE_OPENAI_ENDPOINT -ApiKey YOUR_AZURE_OPENAI_API_KEY
```

## Bash
Use the [DeploySK-Existing-AzureOpenAI.sh](DeploySK-Existing-AzureOpenAI.sh) file found in this folder:
```bash
chmod +x ./DeploySK-Existing-AzureOpenAI.sh
./DeploySK-Existing-AzureOpenAI.sh -d YOUR_DEPLOYMENT_NAME -s YOUR_SUBSCRIPTION_ID -e YOUR_AZURE_OPENAI_ENDPOINT -o YOUR_AZURE_OPENAI_API_KEY
```

## Azure Portal
You can also deploy by clicking on:

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fmicrosoft%2Fsemantic-kernel%2Fmain%2Fsamples%2Fapps%2Fcopilot-chat-app%2Fwebapi%2FDeploymentTemplates%2Fsk-existing-azureopenai.json)


# Deploying with an existing OpenAI account
## PowerShell
Use the [DeploySK-Existing-OpenAI.ps1](DeploySK-Existing-OpenAI.ps1) file found in this folder:
```powershell
.\DeploySK-Existing-OpenAI.ps1 -DeploymentName YOUR_DEPLOYMENT_NAME -Subscription YOUR_SUBSCRIPTION_ID
```

After entering the command above, you will be prompted to enter your OpenAI API key. (You can also pass in the API key using the -ApiKey parameter)

## Bash
After ensuring DeploySK-Existing-OpenAI.sh file found in this folder is executable, enter the following command:

```bash
./DeploySK-Existing-AI.sh -d YOUR_DEPLOYMENT_NAME -s YOUR_SUBSCRIPTION_ID -o YOUR_OPENAI_API_KEY
```

## Azure Portal
You can also deploy by clicking on:

[![Deploy to Azure](https://aka.ms/deploytoazurebutton)](https://portal.azure.com/#create/Microsoft.Template/uri/https%3A%2F%2Fraw.githubusercontent.com%2Fmicrosoft%2Fsemantic-kernel%2Fmain%2Fsamples%2Fapps%2Fcopilot-chat-app%2Fwebapi%2FDeploymentTemplates%2Fsk-existing-openai.json)


# Verifying the deployment
To make sure your web app service is running, go to <!-- markdown-link-check-disable -->https://YOUR_INSTANCE_NAME.azurewebsites.net/probe<!-- markdown-link-check-enable-->

To get your instance's URL, click on the "Go to resource group" button you see at the end of your deployment. Then click on the resource whose name starts with "app-".

This will bring you to the Overview page on your web service. Your instance's URL is the value that appears next to the "Default domain" field.


# Changing your configuration, monitoring your deployment and troubleshooting
From the page just mentioned in the section above, you can change your configuration by clicking on the "Configuration" item in the "Settings" section of the left pane.

Scrolling down in that same pane to the "Monitoring" section gives you access to a multitude of ways to monitor your deployment.

In addition to this, the "Diagnose and "solve problems" item near the top of the pane can yield crucial insight into some problems your deployment may be experiencing.

If the service itself if functioning properly but you keep getting errors (perhaps reported as 400 HTTP errors) when making calls to the Semantic Kernel,
check that you have correctly entered the values for the following settings:
- AIService:AzureOpenAI
- AIService:Endpoint
- AIService:Models:Completion
- AIService:Models:Embedding
- AIService:Models:Planner

AIService:Endpoint is ignored for OpenAI instances from [openai.com](https://openai.com) but MUST be properly populated when using Azure OpenAI instances.

# Authorization
All of the server's endpoints other than the /probe one require authorization to access.
By default, the deployment templates set up the server so that an API key is required to access its endpoints.

AAD authentication and authorization can also be set up manually after the automated deployment is done.

To view the API key required by your instance, access the page for your Semantic Kernel app service in the Azure portal.
From that page, click on the "Configuration" item in the "Settings" section of the left pane. Then click on the text that reads "Hidden value.
Click to show value" next to the "Authorization:ApiKey" setting.

To authorize requests with the API key, it must be added as the value of an "x-sk-api-key" header added to the requests.

# Using web frontends to access your deployment
Make sure to include your frontend's URL as an allowed origin in your deployment's CORS settings. Otherwise, web browsers will refuse to let JavaScript make calls to your deployment.

To do this, go on the Azure portal, select your Semantic Kernel App Service, then click on "CORS" under the "API" section of the resource menu on the left of the page.
This will get you to the CORS page where you can add your allowed hosts.

# Deploying your custom version of Semantic Kernel
You can build and upload a customized version of the Semantic Kernel service.

You can use the standard methods available to [deploy an ASP.net web app](https://learn.microsoft.com/en-us/azure/app-service/quickstart-dotnetcore?pivots=development-environment-vs&tabs=net70) in order to do so.

Alternatively, you can follow the steps below to manually build and upload your customized version of the Semantic Kernel service to Azure.

Modify the code to your needs (for example, by adding your own skills). Once that is done, go into the ../semantic-kernel/samples/apps/copilot-chat-app/webapi
directory and enter the following command:
```powershell
dotnet publish CopilotChatWebApi.csproj --configuration Release --arch x64 --os win
```

This will create the following directory, which will contain all the files needed for a deployment:
../semantic-kernel/samples/apps/copilot-chat-app/webapi/bin/Release/net6.0/win-x64/publish

Zip the contents of that directory then put the resulting zip file on the web.

Put its URI in the "Package Uri" field in the web deployment page you access through the "Deploy to Azure" buttons above, or use its URI as the value for the PackageUri parameter of the Powershell scripts above. Make sure that your zip file is publicly readable.

Your deployment will then use your customized deployment package.


# Cleaning up
Once you are done with your resources, you can delete them from the Azure portal. You can also simply delete the resource group in which they are from the portal or through the
following [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/) command:
```powershell
az group delete --name YOUR_RESOURCE_GROUP
```