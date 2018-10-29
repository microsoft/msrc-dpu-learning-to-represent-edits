#/usr/bash
set -e
REPO_DIR=repos

mkdir -p ${REPO_DIR}
REPO_DIR=$(readlink -f ${REPO_DIR})

python crawl_github.py sampled_repos.txt ${REPO_DIR}

cd DumpCommitData

python extract_commits.py --output=commit_data.jsonl ${REPO_DIR}

dotnet clean

dotnet run -c release get_python_input commit_data.jsonl github_commits.dataset.jsonl grammar.full.json
