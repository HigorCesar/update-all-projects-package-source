# prototype to Update package source from all .net repos in bitbucket account(.NET full framework)  

## How it works  
remoteRepository  
|> cloneRepository  
|> checkoutNewBranch  
|> removeOldPackageSource  
|> addNewPackageSource  
|> stage  
|> commit  
|> log remoteRepository  
|> pushBranch  
|> dispose  

## How to configure  
1. Set bitbucket credentials(bitbucketUsername,bitbucketPassword)  
2. Set name/key from nuget source to replace(packageSourceToUpdate)  
3. Set path to repository address(repositoryBaseUrl)  
4. Set signature(author)  

