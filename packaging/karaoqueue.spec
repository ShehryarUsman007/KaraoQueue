# RPM package for KaraoQueue.
# Repackages the already-published self-contained build (no compiling here).
# Build:  rpmbuild -bb --define "_version 1.0.0" --define "_appdir <published linux-x64 folder>" --define "_pkgdir <packaging folder>" karaoqueue.spec

Name:           karaoqueue
Version:        %{_version}
Release:        1
Summary:        Shuffle and track the karaoke singer rotation
License:        Apache-2.0
URL:            https://github.com/ShehryarUsman007/Karaoke-Shuffler
ExclusiveArch:  x86_64

# version 1.0.0 was called karaoke-shuffler: installing this one replaces it
Obsoletes:      karaoke-shuffler < 1.1.0

# .NET is bundled; these are the system libraries the app's UI needs
Requires:       libX11 libXext libXfixes libXi libXrandr fontconfig freetype libicu xdg-utils

# prebuilt .NET files: don't strip them, scan them for dependencies or make debug packages
AutoReqProv:    no
%global debug_package %{nil}
%global __os_install_post %{nil}

%description
A very basic karaoke singer rotation app, made for a karaoke group in Windsor, Ontario.
Shuffles the singers, highlights whose turn it is and handles late arrivals.

%install
mkdir -p %{buildroot}/opt/karaoqueue %{buildroot}%{_bindir}
cp -a %{_appdir}/. %{buildroot}/opt/karaoqueue/
ln -s /opt/karaoqueue/KaraoQueue %{buildroot}%{_bindir}/karaoqueue
install -Dm644 %{_pkgdir}/karaoqueue.desktop %{buildroot}%{_datadir}/applications/karaoqueue.desktop
install -Dm644 %{_pkgdir}/icon.png %{buildroot}%{_datadir}/icons/hicolor/256x256/apps/karaoqueue.png
install -Dm644 %{_pkgdir}/icon.svg %{buildroot}%{_datadir}/icons/hicolor/scalable/apps/karaoqueue.svg

%files
/opt/karaoqueue
%{_bindir}/karaoqueue
%{_datadir}/applications/karaoqueue.desktop
%{_datadir}/icons/hicolor/256x256/apps/karaoqueue.png
%{_datadir}/icons/hicolor/scalable/apps/karaoqueue.svg
