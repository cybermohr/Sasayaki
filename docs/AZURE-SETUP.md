# Azure setup for Sasayaki

Sasayaki uses two model deployments in Microsoft Foundry:

| Purpose | Model | Deployment name |
| --- | --- | --- |
| Live English transcription | `gpt-live-transcribe` | `sasayaki-speech` |
| Light text cleanup | `gpt-5.4-mini` | `sasayaki-cleanup` |

You need an Azure subscription and permission to create resources, deploy models,
and read resource keys. The app uses API-key authentication. Enter credentials
in the app's Settings; no separate configuration file is needed. Saved keys are
protected for your Windows account outside this repository.

## Step-by-step setup

1. **Check model availability and quota before choosing a region.**

   Install the [Azure CLI](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli-windows),
   open PowerShell, and select your subscription:

   ```powershell
   az login
   az account list --query "[].{Name:name,Subscription:id}" --output table
   az account set --subscription 'YOUR-SUBSCRIPTION-ID'
   az account show --query "{Name:name,Subscription:id,Tenant:tenantId}" --output table
   ```

   Start with **East US 2**; check **Central US** as a US alternative. Use it only
   if the model and quota checks below pass for your subscription. These
   read-only commands show only `gpt-live-transcribe` and its matching quota:

   ```powershell
   foreach ($candidateRegion in @('eastus2', 'centralus')) {
       Write-Output "Region: $candidateRegion"
       $catalogJson = az cognitiveservices model list --location $candidateRegion --output json
       if ($LASTEXITCODE -ne 0) { throw "Cannot read model catalog for $candidateRegion." }
       $catalog = $catalogJson | ConvertFrom-Json
       $speechModels = @($catalog | Where-Object { $_.model.name -eq 'gpt-live-transcribe' })
       if ($speechModels.Count -eq 0) {
           Write-Output 'gpt-live-transcribe is not listed in this region.'
           continue
       }
       $modelRows = @($speechModels | ForEach-Object {
           $speechModel = $_.model
           foreach ($deploymentSku in $speechModel.skus) {
               [pscustomobject]@{
                   Model = $speechModel.name
                   Version = $speechModel.version
                   Lifecycle = $speechModel.lifecycleStatus
                   Format = $speechModel.format
                   SKU = $deploymentSku.name
                   UsageName = $deploymentSku.usageName
               }
           }
       })
       $modelRows | Format-Table -AutoSize -Wrap

       $usageJson = az cognitiveservices usage list --location $candidateRegion --output json
       if ($LASTEXITCODE -ne 0) { throw "Cannot read quota for $candidateRegion." }
       $usageNames = @($modelRows.UsageName | Where-Object { $_ })
       $quotaRows = @(($usageJson | ConvertFrom-Json) | Where-Object { $_.name.value -in $usageNames })
       if ($quotaRows.Count -eq 0) {
           Write-Output 'No matching quota entry returned; confirm quota in Foundry or with Azure support.'
       } else {
           $quotaRows | Select-Object @{Name='Quota';Expression={$_.name.value}},
               currentValue, limit, @{Name='Remaining';Expression={$_.limit - $_.currentValue}} |
               Format-Table -AutoSize -Wrap
       }
   }
   ```

   Use a region that lists the model and has enough remaining quota for the
   deployment size you plan to select. **Limit** is the total allocation for that
   quota entry; **currentValue** is the allocation already used by deployments;
   **Remaining** is `limit - currentValue`. These are deployment-capacity units,
   not a spending limit or count of completed transcriptions.

   If a model or quota entry is missing, confirm availability in Foundry's
   **Manage > Quota** or with your subscription administrator before continuing.
   Reading usage may require **Cognitive Services Usages Reader** at subscription
   scope. A Global quota pool is shared across regions. Catalog and quota checks
   do not reserve capacity; deployment confirms that capacity is available.
   Also confirm `gpt-5.4-mini` is available in your chosen region if you want both
   deployments in one resource.

2. **Create a resource group.**

   In [Azure portal](https://portal.azure.com/), open **Resource groups > Create**.
   Select your subscription, enter `rg-sasayaki`, and choose the region from
   step 1. Select **Review + create**, then **Create**.

3. **Create the Microsoft Foundry resource.**

   Open the [Microsoft Foundry resource wizard](https://portal.azure.com/#create/Microsoft.CognitiveServicesAIFoundry).
   Use these selections for a personal desktop setup:

   | Tab / field | Select or enter |
   | --- | --- |
   | Basics / Subscription | Your intended Azure subscription. |
   | Basics / Resource group | `rg-sasayaki`, created in step 2. |
   | Basics / Name | `sasayaki-bmohr`. If unavailable, try `sasayaki-bmohr-01`. |
   | Basics / Region | The region checked above: **East US 2** or **Central US**. Complete the availability/quota check before selecting it. |
   | Basics / Default project name | `sasayaki`. Keep creation of the default project enabled if there is a checkbox. |
   | Basics / Pricing tier, if shown | **Standard S0**. Model deployment pricing is selected separately later. |
   | Storage / Credential storage | Leave the default Microsoft-managed configuration. If an optional **Key Vault** selector shows **None**, leave it at **None**. |
   | Storage / Application logging | Leave the default. If an optional **Application Insights** selector shows **None**, leave it at **None**. |
   | Storage / Agent service | Keep the default/basic managed setup. Leave **Select Resources** unchecked; leave custom Cosmos DB, AI Search, and Storage fields unselected. |
   | Storage / Speech and Language service | Leave **Storage Account (preview)** unselected (**None**, if offered). |
   | Inbound Networking / Public network access | **Enabled / All networks**, whichever wording is shown. Leave private endpoint connections empty. |
   | Outbound Networking / Agent outbound settings / Network isolation mode for Agent | Select **No Outbound Networking**. Leave **Custom VNet** unselected. |
   | Identity / Identity type | **System assigned**. Set its status to **On** if the wizard offers a toggle. Leave user-assigned identities unselected. |
   | Encryption / Data Encryption | Leave **Encrypt data using a customer-managed key** unchecked. The resource uses **Microsoft-managed keys** by default. |
   | Tags | Optional; leave blank or add `application = sasayaki`. |

   Use your own unique resource name in place of `sasayaki-bmohr` if needed.
   Optional storage and logging resources are not required for Sasayaki's direct
   speech and cleanup requests. **No Outbound Networking** configures the Agent
   service; it does not block the app's inbound connection. The resource's managed
   identity does not replace the API keys used by the app.

   Select **Review + create**, check your selections, accept any required terms,
   and select **Create**. Wait for deployment to finish. Open
   [Microsoft Foundry](https://ai.azure.com/), select the resource and its default
   project `sasayaki`; use that project rather than creating a second one.

4. **Deploy the speech model.**

   In Foundry, open **Build > Models > Deploy a base model**. Search for
   **gpt-live-transcribe**, open it, and select **Deploy**. Use:

   | Field | Selection |
   | --- | --- |
   | Deployment name | `sasayaki-speech` |
   | Deployment type | **Global Standard**, where offered |
   | Model version | A supported version offered in your subscription |
   | Capacity / rate limit | An allocation within your available quota for personal use |

   Review the price and select **Deploy**. Wait for provisioning to succeed.
   The app uses this model through the GPT Realtime transcription API.

5. **Deploy the cleanup model.**

   Return to **Deploy a base model**, search for **gpt-5.4-mini**, and deploy it
   as `sasayaki-cleanup`. Select **Global Standard** where offered and an
   allocation within your quota for short personal dictation requests.

   If this model is unavailable in the speech resource's region, create a second
   Foundry resource in `rg-sasayaki` in a supported region and deploy cleanup
   there. The app supports separate resources for speech and cleanup.

6. **Gather the connection values.**

   Open **Build > Models > sasayaki-speech > Details**, then the equivalent
   **Details** page for `sasayaki-cleanup`. Use the endpoint and key shown on each
   page. The resource's **Keys and Endpoint** page is another source for these
   values; you do not need separate deployment-specific keys.

   Keep the pages available while entering these six values in Settings:

   | App field | Value |
   | --- | --- |
   | Speech resource root | `https://YOUR-SPEECH-RESOURCE.openai.azure.com/` |
   | Speech deployment | `sasayaki-speech` |
   | Speech API key | Key shown on the speech deployment's Details page |
   | Cleanup base URL | `https://YOUR-CLEANUP-RESOURCE.services.ai.azure.com/openai/v1/` |
   | Cleanup deployment | `sasayaki-cleanup` |
   | Cleanup API key | Key shown on the cleanup deployment's Details page |

   For **speech**, use the resource root only: remove any path and query string
   from a full API URL. The app constructs the WebSocket route itself.

   For **cleanup**, keep the base URL ending in `/openai/v1/`. If the displayed
   endpoint ends in `/openai/v1/responses` or `/openai/v1/chat/completions`, remove
   the final operation name. The `.openai.azure.com/openai/v1/` host format also
   works for cleanup.

   If both deployments belong to one resource, use that resource's key in both
   key fields. If they belong to separate resources, use each resource's own key.
   Enter the actual deployment names if you chose different names above.

7. **Configure and run Sasayaki.**

   Install the MSI using the [README instructions](../README.md#install-update-remove),
   then open Sasayaki from the Start menu. Settings opens when configuration is
   missing or invalid. Enter the six connection values from step 6, select your
   microphone, and choose your recording shortcut. The default is **Ctrl+Win**.

   Select **Test Azure connections (uses cleanup tokens)**, then **Save settings**.
   The connection check verifies the speech session and a cleanup request.
   To confirm microphone transcription, focus an empty Notepad document, hold
   your recording shortcut, speak a short phrase, and release it. You can also
   double-press the shortcut to start and stop hands-free recording.

   Future launches with valid saved settings start quietly in the system tray.
   Right-click the tray icon and select **Settings** to change endpoints, keys,
   deployments, microphone, or shortcut; choose **Quit** to close the app.
   Leave key fields blank to keep saved keys, or enter replacement keys to update
   them. Recording mutes system playback and restores it when capture ends.

8. **Set a budget and review usage.**

   In Azure portal, open **Cost Management > Budgets** at your subscription or
   resource-group scope. Create a monthly budget, such as $10, and alerts at
   50%, 80%, and 100%. Budget alerts notify you; they do not stop spending.

   The East US 2 price shown for **gpt-live-transcribe** is **$1.02 per unit-hour**
   as of October 8, 2026. Speech billing is based on audio duration. At that rate,
   10 billed audio hours cost `10 × $1.02 = $10.20`; a two-minute dictation costs
   about `$1.02 × 2/60 = $0.034`. Leaving the app open is not billed audio time.
   These estimates exclude cleanup token charges, taxes, and other Azure charges.
   Confirm the current rate, billing unit, and rounding in your deployment's
   pricing view and Cost Management.

## References

- [Foundry resource creation](https://learn.microsoft.com/en-us/azure/ai-services/multi-service-resource?pivots=azportal)
- [Model catalog and region availability](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure)
- [Subscription model listing](https://learn.microsoft.com/en-us/cli/azure/cognitiveservices/model?view=azure-cli-latest)
- [Subscription usage listing](https://learn.microsoft.com/en-us/cli/azure/cognitiveservices/usage?view=azure-cli-latest)
- [Quota management](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/quota)
- [Foundry endpoints](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/endpoints)
- [GPT Realtime transcription](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/realtime-audio)
- [GPT Realtime WebSocket setup](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/realtime-audio-websockets)
- [Budget setup](https://learn.microsoft.com/en-us/azure/cost-management-billing/costs/tutorial-acm-create-budgets)
