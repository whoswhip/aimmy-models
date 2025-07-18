# A list of all search filters
## !unique
Include `!unique` to ensure there are no duplicate models, this is based off of the githash github automatically creates.
## sort:size
Including `sort:size` will sort the files ascending (small to large) based off their file size.
## sort:-size
Including `sort:-size` will sort the files descending (large to small) based off their file size:
## githash:
Including `githash:{hash}` will only show files with that hash.
## imagesize:
Including `imagesize:{size}` will show any models that have that image size.
The search accepts `:`, `>`, `>=`, `<` and `<=`
## labels:
Including `labels:{amount}` will only show models that have that amount of labels.
The search accepts `:`, `>`, `>=`, `<` and `<=`
## repo:
Including `repo:{name}` will filter files by repository. You can use:
- `repo:aimmy-models` - Show files from any repository named "aimmy-models"
- `repo:whoswhip/aimmy-models` - Show files specifically from "whoswhip/aimmy-models"
- `repo:babyhamsta` - Show files from any repository containing "babyhamsta"
## dynamic:(true/false)
Including `dynamic:(true/false)` will only show models that have been exported as dynamic.
## simplified:(true/false)
Including `simplified:(true/false)` will only show models that have been exported with the simplify arguement.