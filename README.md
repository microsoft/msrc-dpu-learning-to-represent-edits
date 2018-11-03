## Learning to Represent Edits

This repo contains scripts to extract the Github code edits datasets used in "[Learning to Represent Edits](https://arxiv.org/abs/1810.13337)" by Yin et al., 2018.

### Usage

First, create a conda environment that includes all required libraries.

```bash
conda env create -f environment.yml

source activate github_edits  # activate the environment
```

You also need to install [dotnet core 2.1](https://www.microsoft.com/net/download).

Run the script `run.sh` in the repo's root folder

```bash
./run.sh
```

This script will (1) crawl the Github to clone repos listed in `sampled_repos.txt`, 
(2) extract commits using `DumpCommitData/extract_commits.py`; 
(3) filter the extracted commits and perform cannonicalization, and extract the Abstract Syntax Tree of the previous and updated code in a commit (e.g., renaming locally defined variables)

The final output file `DumpCommitData/github_commits.dataset.jsonl` is a 
[jsonl](http://jsonlines.org/) file, with each line consisting of a json-serialized entry. The format is:


| Field                  | Description                                                                  |
|------------------------|------------------------------------------------------------------------------|
| Id                     | Id of the entry, format is `{ProjectName}|{CommitSHA}|{FileEdited}_{EditId}` |
| PrevCodeChunk          | Untokenized previous code (i.e., code before editing)                        |
| UpdatedCodeChunk       | Untokenized updated code (i.e., code after editing)                          |
| PrevCodeChunkTokens    | Tokenized previous code                                                      |
| UpdatedCodeChunkTokens | Tokenized updated code                                                       |
| PrevCodeAST            | Json-serialized Abstract Syntax Tree of the previous code                    |
| UpdatedCodeAST         | Json-serialized Abstract Syntax Tree of the updated code                     |
| PrecedingContext       | Tokenized 3 lines of code before the edit                                    |
| SucceedingContext      | Tokenized 3 lines of code after the edit                                     |

### Citing

If you use this extractor in an academic work, please consider citing
```

@article{yin2018learning,
   author = {{Yin}, P. and {Neubig}, G. and {Allamanis}, M. and {Brockschmidt}, M. and {Gaunt}, A.~L.},
   title = "{Learning to Represent Edits}",
   journal = {ArXiv e-prints},
   archivePrefix = "arXiv",
   eprint = {1810.13337},
   year = 2018,
   month = oct,
}
```

# Contributing

This project welcomes contributions and suggestions.  Most contributions require you to agree to a
Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us
the rights to use your contribution. For details, visit https://cla.microsoft.com.

When you submit a pull request, a CLA-bot will automatically determine whether you need to provide
a CLA and decorate the PR appropriately (e.g., label, comment). Simply follow the instructions
provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.
