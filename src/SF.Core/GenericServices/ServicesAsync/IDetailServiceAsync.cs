﻿/*******************************************************************************
* �����ռ�: SF.Core.GenericServices.Base
*
* �� �ܣ� N/A
* �� ���� ICreateService
*
* Ver ������� ������ �������
* ����������������������������������������������������������������������
* V0.01 2016/12/8 17:25:54 ������� ����
*
* Copyright (c) 2016 SimpleFramework ��Ȩ����
* Description: SimpleFramework���ٿ���ƽ̨
* Website��http://www.mayisite.com
*********************************************************************************/
using System.Threading.Tasks;

namespace SF.Core.GenericServices.ServicesAsync
{
    public interface IDetailServiceAsync
    {
        /// <summary>
        /// This returns a status which, if Valid, contains a single entry found using its primary keys.
        /// </summary>
        /// <typeparam name="T">The type of the data to output. 
        /// Type must be a type either an EF data class or one of the EfGenericDto's</typeparam>
        /// <param name="keys">The keys must be given in the same order as entity framework has them</param>
        /// <returns>Status. If valid Result holds data (not tracked), otherwise null</returns>
        Task<ISuccessOrErrors<T>> GetDetailAsync<T>(params object[] keys) where T : class, new();
    }
}