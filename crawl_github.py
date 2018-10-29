import os
import sys


repo_file = sys.argv[1]
repo_folder = sys.argv[2]

repo_urls = [line.strip().split('\t')[1] for line in open(repo_file)]

os.chdir(repo_folder)

for repo_url in repo_urls:
    cmd = 'git clone %s.git' % repo_url
    print('cloning %s' % repo_url, file=sys.stderr)
    os.system(cmd)
