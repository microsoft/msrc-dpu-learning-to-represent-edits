#!/usr/bin/env python
"""
Usage:
    extract_commits.py [options] REPOS_DIR

Options:
    -h --help                  Show this screen.
    --output=<filename>        The filename for the output html. [default: output.jsonl]
    --repo_list=<filename>     List of repos to extract commits from [default: None]
    --single_place_commit       Only collect commit that modifies a single place
"""

import os, sys
import json
from collections import OrderedDict

from git import *
from docopt import docopt


def is_valid_comment_file(file_path: str) -> bool:
    # only get .cs file
    return file_path.endswith('.cs')


def extract_revision_from_repos(args):
    repos_folder = args['REPOS_DIR']
    f_out = open(args['--output'], 'w', encoding='utf-8')

    total_commits = 0
    repo_folders = list(filter(lambda x: os.path.isdir(os.path.join(repos_folder, x)), os.listdir(repos_folder)))
    if args['--repo_list'] != 'None':
        valid_repos = [l.strip() for l in open(args['--repo_list'])]
        repo_folders = [folder for folder in repo_folders if folder in valid_repos]
    print('Processing %d repos' % len(repo_folders), file=sys.stderr)

    for repo_folder in repo_folders:
        print('Counting commits in repo %s' % repo_folder, file=sys.stderr)
        repo = Repo(path=os.path.join(repos_folder, repo_folder))

        try:
            repo_valid_commit_num = len([commit for commit in repo.iter_commits() if len(commit.parents) == 1])
            total_commits += repo_valid_commit_num
        except:
            print('Error in counting commits in repo %s' % repo_folder, file=sys.stderr)
            del repo_folders[repo_folders.index(repo_folder)]

    print('Total number of commits: %d' % total_commits, file=sys.stderr)

    for repo_folder in repo_folders:
        repo = Repo(path=os.path.join(repos_folder, repo_folder))
        print('Processing repo %s' % repo_folder, file=sys.stderr)

        for commit in list(repo.iter_commits()):
            if len(commit.parents) != 1:
                continue

            parent_commit = commit.parents[0]

            if args['--single_place_commit']:
                diffs = list(parent_commit.diff(commit))
                if len(diffs) == 1:
                    diff_modified = diffs[0]
                    if diff_modified.change_type == 'M' and diff_modified.a_path.endswith('.cs') and diff_modified.b_path.endswith('.cs'):
                        prev_file_content = diff_modified.a_blob.data_stream.read()
                        if prev_file_content:
                            prev_file_content = prev_file_content.decode("utf-8", errors="ignore")

                        updated_file_content = diff_modified.b_blob.data_stream.read()
                        if updated_file_content:
                            updated_file_content = updated_file_content.decode("utf-8", errors="ignore")

                        if prev_file_content and updated_file_content and prev_file_content != updated_file_content:
                            revision_id = '|'.join([repo_folder, str(commit.hexsha), diff_modified.a_blob.path])

                            print('\t writing one revision [%s]' % revision_id, file=sys.stderr)
                            entry = OrderedDict(id=revision_id,
                                                prev_file=prev_file_content,
                                                updated_file=updated_file_content,
                                                message=commit.message.strip())

                            f_out.write(json.dumps(entry) + '\n')
            else:
                for diff_modified in parent_commit.diff(commit).iter_change_type('M'):
                    if diff_modified.a_path.endswith('.cs') and diff_modified.b_path.endswith('.cs'):
                        prev_file_content = diff_modified.a_blob.data_stream.read()
                        if prev_file_content:
                            prev_file_content = prev_file_content.decode("utf-8", errors="ignore")

                        updated_file_content = diff_modified.b_blob.data_stream.read()
                        if updated_file_content:
                            updated_file_content = updated_file_content.decode("utf-8", errors="ignore")

                        if prev_file_content and updated_file_content and prev_file_content != updated_file_content:
                            # print('parent commit: %s' % parent_commit.hexsha)
                            # print('this commit: %s' % commit.hexsha)
                            # print(diff_modified.a_blob.path)
                            # print(diff_modified.a_blob.data_stream.read())
                            # print(diff_modified.b_blob.data_stream.read())

                            revision_id = '|'.join([repo_folder, str(commit.hexsha), diff_modified.a_blob.path])

                            print('\t writing one revision [%s]' % revision_id, file=sys.stderr)
                            entry = OrderedDict(id=revision_id, prev_file=prev_file_content, updated_file=updated_file_content)

                            f_out.write(json.dumps(entry) + '\n')

    f_out.close()


if __name__ == '__main__':
    args = docopt(__doc__)
    extract_revision_from_repos(args)
