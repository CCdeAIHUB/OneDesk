package IPC::Cmd;

use strict;
use warnings;
use File::Spec;

# Git for Windows 的精简 Perl 缺少 IPC::Cmd 的间接依赖。OpenSSL 在 Android
# 配置阶段只需要 can_run，因此提供同名的最小兼容实现，避免混入另一套 Perl 运行时。
sub import { }

sub can_run {
    my ($command) = @_;
    return undef if !defined($command) || $command eq '';

    if (File::Spec->file_name_is_absolute($command)) {
        return _first_executable($command);
    }

    for my $directory (File::Spec->path()) {
        my $candidate = File::Spec->catfile($directory, $command);
        my $resolved = _first_executable($candidate);
        return $resolved if defined($resolved);
    }

    return undef;
}

sub _first_executable {
    my ($candidate) = @_;
    for my $suffix ('', '.exe', '.cmd', '.bat') {
        my $path = "$candidate$suffix";
        return $path if -f $path && -x $path;
    }
    return undef;
}

1;
