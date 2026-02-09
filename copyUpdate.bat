@echo off
setlocal

:: 源路径（注意：不要以 \ 结尾）
set "SOURCE=e:\Works_e\Work_YD\victools"
set "DEST=."

:: 获取当前批处理文件名（支持重命名）
set "SELF=%~nx0"

echo 源路径: "%SOURCE%"
echo 当前脚本: %SELF%
echo.

if not exist "%SOURCE%" (
    echo 错误：源目录不存在！
    pause
    exit /b 1
)

:: robocopy：排除 .git 目录 和 当前 bat 文件
robocopy "%SOURCE%" "%DEST%" /E /COPYALL /IS /IT /R:0 /W:0 ^
  /XD ".git" ^
  /XF "%SELF%" ^
  /NS /NC /NDL /NJH /NJS > "%temp%\robocopy_files.tmp"

:: 统计并显示结果
set "fileCount=0"
for /f %%i in ('type "%temp%\robocopy_files.tmp" ^| find /v /c ""') do set "fileCount=%%i"

if %fileCount% GTR 0 (
    echo 已覆盖或新增以下文件：
    echo ========================
    type "%temp%\robocopy_files.tmp"
    echo ========================
    echo 共 %fileCount% 个文件已同步。
) else (
    echo 无文件需要更新。
)

del "%temp%\robocopy_files.tmp" >nul 2>&1

if %ERRORLEVEL% LEQ 1 (
    echo.
    echo 同步完成！
) else (
    echo.
    echo 警告：同步过程中出现错误（代码 %ERRORLEVEL%）。
)

pause