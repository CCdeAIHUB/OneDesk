package Pod::Usage;

use strict;
use warnings;

# configdata.pm 会无条件加载 Pod::Usage，但正常生成 Makefile 时不会进入帮助分支。
# 保留兼容导出；若误入帮助分支则明确失败，避免静默产生不完整构建结果。
sub import {
    my $caller = caller;
    no strict 'refs';
    *{$caller . '::pod2usage'} = \&pod2usage;
}

sub pod2usage {
    die "当前精简 Git Perl 不支持 OpenSSL 的命令行帮助输出\n";
}

1;
