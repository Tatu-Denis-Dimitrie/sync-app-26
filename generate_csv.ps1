$headers = "Id,FirstName,LastName,Email,DepartmentId,DepartmentName,AssignedToId,AssignedToName,Role"
$csv = @($headers)
$users = 1..10000 | ForEach-Object {
    "$_,User$_,Test$_,user$_@example.com,D1,Engineering,M1,Manager1,Employee"
}
$csv += $users
$csv | Out-File -Encoding utf8 "users_10k.csv"
Write-Host "Generated users_10k.csv with 10,000 records."
