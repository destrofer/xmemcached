# Please note that there is an issue when compiling under Windows:
# Microsoft .NET csc compiler crashes when trying to output anything
# to stdout or stderr while being called by GNU make tool. This
# prevents you from seing any errors or warnings when compiling.
# If you had a crash then most likely there is some error in the code.
find = $(foreach dir,$(1),$(foreach d,$(wildcard $(dir)/*),$(call find,$(d),$(2))) $(wildcard $(dir)/$(strip $(2))))
unique = $(if $(1),$(strip $(word 1,$(1)) $(call unique,$(filter-out $(word 1,$(1)),$(1)))))
path = $(subst /,$(PATHSYMBOL),$(1))
empty := 
space := $(empty) $(empty)
escspace = $(subst $(space),\$(space),$(1))
filelist = $(foreach FILE,$(2),$(call escspace,$(1)/$(FILE)))

# .NET Framework versions: v1.0.3705, v1.1.4322, v2.0.50727, v3.0, v3.5, v4.0.30319
FRAMEWORK := v4.0.30319
CSCFLAGS := /t:exe /nologo /noconfig /unsafe /nostdlib+ /optimize+ /debug+ /d:DEBUG /warnaserror
REFERENCES := mscorlib.dll\
	Mono.Posix.dll\
	System.dll\
	System.Drawing.dll\
	System.Configuration.dll\
	System.Configuration.Install.dll\
	System.Core.dll\
	System.Data.dll\
	System.Data.DataSetExtensions.dll\
	System.Net.dll\
	System.ServiceProcess.dll\
	System.Xml.dll\
	System.Xml.Linq.dll\
	System.Numerics.dll

IMPORTANT_DLL_FILES := mscorlib.dll Mono.Posix.dll

SERVICE_NAME := xmemcached

EXE_SRCDIR := src
EXE_OUTDIR := bin
CFG_SRCDIR := conf
SCP_SRCDIR := scripts

EXE_FILE := $(SERVICE_NAME).exe
CFG_FILE := $(SERVICE_NAME).conf
SCP_FILE := $(SERVICE_NAME)

SOURCES := $(call find, $(EXE_SRCDIR), *.cs)

ifdef SystemRoot
	PATHSYMBOL := $(subst /,\,/)
	LIB_DIR := $(ProgramFiles)/Mono-2.10.9/lib/mono/4.0
	FRAMEWORK_DIR := $(SystemRoot)\Microsoft.NET\Framework\$(FRAMEWORK)
	
	touch = copy /b $(1)+,, $(1)>nul
	cp = copy /Y $(1) $(2)
	cpnx = if not exist $(2) copy $(1) $(2)
	cpask = copy /-Y $(1) $(2)
	RM := del
	CHMOD := rem
	MKDIR := mkdir
	COMPILER := csc.exe
	INSTALL_UTIL := $(FRAMEWORK_DIR)\InstallUtil.exe

	PLATFORM_CSFLAGS := /platform:anycpu /d:WIN32 /errorreport:none /warn:0

	EXE_DSTDIR := $(SystemDrive)/services
	CFG_DSTDIR := $(SystemDrive)/services
	SCP_DSTDIR := $(SystemDrive)/services
	
	DBG_OUTFILE := $(EXE_OUTDIR)/$(SERVICE_NAME).pdb
	
	DLL_DSTFILES := $(addprefix $(EXE_DSTDIR)/,$(IMPORTANT_DLL_FILES))

	CSC := $(call path,$(FRAMEWORK_DIR)/$(COMPILER))
else
	PATHSYMBOL := /
	LIB_DIR := /usr/lib/mono/4.0
	FRAMEWORK_DIR := $(LIB_DIR)
	
	touch = touch $(1)
	cp = yes y | cp -i $(1) $(2)
	cpnx = yes n | cp -i $(1) $(2)
	cpask = cp -i $(1) $(2)
	RM := rm
	CHMOD := chmod
	MKDIR := mkdir
	COMPILER := dmcs

	PLATFORM_CSFLAGS := /sdk:4 /platform:anycpu /d:LINUX /warn:4

	EXE_DSTDIR := /bin
	CFG_DSTDIR := /etc/$(SERVICE_NAME)
	SCP_DSTDIR := /etc/init.d
	
	DBG_OUTFILE := $(EXE_OUTDIR)/$(EXE_FILE).mdb

	REFERENCES := $(addprefix $(LIB_DIR)/,$(REFERENCES))
	CSC := $(COMPILER)
endif

DLL_OUTFILES := $(call filelist,$(EXE_OUTDIR),$(IMPORTANT_DLL_FILES))
DLL_SRCFILES := $(call filelist,$(LIB_DIR),$(IMPORTANT_DLL_FILES))

EXE_OUTFILE := $(EXE_OUTDIR)/$(EXE_FILE)
CFG_SRCFILE := $(CFG_SRCDIR)/$(CFG_FILE)
SCP_SRCFILE := $(SCP_SRCDIR)/$(SCP_FILE)

EXE_DSTFILE := $(EXE_DSTDIR)/$(EXE_FILE)
CFG_DSTFILE := $(CFG_DSTDIR)/$(CFG_FILE)
SCP_DSTFILE := $(SCP_DSTDIR)/$(SCP_FILE)

export LIB = $(call path,$(LIB_DIR))

.PHONY:	make
make:	$(EXE_OUTFILE)
	
clean:
	-$(RM) "$(call path,$(EXE_OUTFILE))"
	-$(RM) "$(call path,$(DBG_OUTFILE))"

update:	$(EXE_DSTFILE) $(CFG_DSTFILE) $(SCP_DSTFILE)
	
uninstall:
ifdef SystemRoot
	-net stop $(SERVICE_NAME)
	-$(INSTALL_UTIL) /Uninstall /LogToConsole=true "$(call path,$(EXE_DSTFILE))"
else
	-$(call path,$(SCP_DSTFILE)) stop
	-$(RM) "$(call path,$(SCP_DSTFILE))"
	-update-rc.d $(SERVICE_NAME) remove
endif
	-$(RM) "$(call path,$(EXE_DSTFILE))"
	
install:	$(EXE_DSTFILE) $(CFG_DSTFILE) $(SCP_DSTFILE)
ifdef SystemRoot
	$(INSTALL_UTIL) /LogToConsole=true "$(call path,$(EXE_DSTFILE))"
else
	update-rc.d $(SERVICE_NAME) defaults 20 80
endif

test:	make
	$(call path,$(EXE_OUTFILE)) -c -C "$(call path,$(CFG_SRCFILE))"
	
all:	clean install

$(EXE_OUTFILE): $(EXE_OUTDIR) $(SOURCES) $(DLL_OUTFILES) $(CFG_SRCFILE)
	$(CSC) /out:$(call path,$(EXE_OUTFILE)) $(CSCFLAGS) $(addprefix /r:,$(REFERENCES)) $(PLATFORM_CSFLAGS) /recurse:$(subst /,\\,$(EXE_SRCDIR)/*.cs)

$(EXE_DSTFILE): $(EXE_DSTDIR) $(EXE_OUTFILE) $(DLL_DSTFILES)
	$(call cp,"$(call path,$(EXE_OUTFILE))","$(call path,$(EXE_DSTFILE))")
	$(CHMOD) +x "$(call path,$(EXE_DSTFILE))"

$(CFG_DSTFILE): $(CFG_DSTDIR) $(CFG_SRCFILE)
	$(call cpask,"$(call path,$(CFG_SRCFILE))","$(call path,$(CFG_DSTFILE))")

$(SCP_DSTFILE): $(SCP_DSTDIR) $(SCP_SRCFILE)
	$(call cp,"$(call path,$(SCP_SRCFILE))","$(call path,$(SCP_DSTFILE))")
	$(CHMOD) +x "$(call path,$(SCP_DSTFILE))"

ifdef DLL_DSTFILES
$(DLL_DSTFILES): $(EXE_DSTDIR) $(DLL_OUTFILES)
	$(call cp,"$(call path,$(EXE_OUTDIR)/$(@F))","$(call path,$(EXE_DSTDIR)/$(@F))")
endif

$(DLL_OUTFILES): $(EXE_OUTDIR) $(DLL_SRCFILES)
	$(call cp,"$(call path,$(LIB_DIR)/$(@F))","$(call path,$(EXE_OUTDIR)/$(@F))")

$(call unique,$(EXE_OUTDIR) $(EXE_DSTDIR) $(CFG_DSTDIR) $(SCP_DSTDIR)):
	$(MKDIR) "$(call path,$@)"

$(SOURCES) $(SCP_SRCFILE) $(CFG_SRCFILE) $(DLL_SRCFILES):
