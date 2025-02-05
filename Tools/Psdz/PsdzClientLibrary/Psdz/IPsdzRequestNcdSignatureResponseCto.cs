﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BMW.Rheingold.Psdz.Model.SecureCoding.SignatureResultCto;

namespace BMW.Rheingold.Psdz.Model.Sfa.RequestNcdSignatureResponseCto
{
    public interface IPsdzRequestNcdSignatureResponseCto
    {
        IList<IPsdzSignatureResultCto> SignatureResultCtoList { get; }

        int DurationOfLastRequest { get; }

        IList<IPsdzSecurityBackendRequestFailureCto> Failures { get; }

        PsdzSecurityBackendRequestProgressStatusToEnum ProgressStatus { get; }
    }
}
